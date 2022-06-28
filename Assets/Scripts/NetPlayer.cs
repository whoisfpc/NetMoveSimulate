using NetMoveSimulate.Network;
using UnityEngine;

namespace NetMoveSimulate
{
	public class NetPlayer : MonoBehaviour
	{
		private enum MoveMode
		{
			Walking,
			Falling,
		}


		public Transform RefRoot { get; private set; }

		public Vector3 NetPosition
		{
			get
			{
				return RefRoot.InverseTransformPoint(transform.position);
			}
			set
			{
				transform.position = RefRoot.TransformPoint(value);
			}
		}

		public Quaternion NetRotation
		{
			get
			{
				return Quaternion.Inverse(RefRoot.rotation) * transform.rotation;
			}
			set
			{
				transform.rotation = Quaternion.Inverse(RefRoot.rotation) * value;
			}
		}

		private Color bodyColor;
		public Color BodyColor
		{
			get
			{
				return bodyColor;
			}
			set
			{
				bodyColor = value;
				bodyRenderer.material.color = value;
			}
		}

		private const float AVOID_COLLIDER_DIST = 0.01f;
		private const int MAX_RESOLVE_OVERLAP_COUNT = 3;

		public float maxSpeed = 5f;
		public float rotateAngleSpeed = 360f;
		public Renderer bodyRenderer;
		public uint id;
		public CapsuleCollider capsule;
		public LayerMask groundMask;
		public float maxStairHeight = 0.3f;

		private Vector2 moveInput;
		private bool jumpInput;
		private Transform inputSpace;
		private bool isLocalPlayer;
		private NetClient client;
		private uint sequence;

		private readonly Collider[] penetrateOverlapCache = new Collider[64];
		private Vector3 gravity = new Vector3(0, -0.98f, 0);
		private Vector3 currentVelocity;
		private MoveMode currentMoveMode = MoveMode.Walking;
		private bool overlapResolved;
		private CapsuleParamsCache capsuleCache;
		private Vector3 groundNormal = Vector3.up;

		private void Awake()
		{
			capsuleCache = new CapsuleParamsCache(capsule);
		}

		public void InitLocal(NetClient client, Transform inputSpace)
		{
			this.client = client;
			RefRoot = client.RefRoot;
			transform.SetParent(RefRoot);
			transform.SetPositionAndRotation(client.spawnPoint.position, client.spawnPoint.rotation);
			this.inputSpace = inputSpace;
			BodyColor = Color.HSVToRGB(Random.value, 1f, 1f);
			isLocalPlayer = true;
		}

		public void InitRemote(NetClient client, PlayerSpawnInfo playerInfo)
		{
			this.client = client;
			id = playerInfo.id;
			RefRoot = client.RefRoot;
			transform.SetParent(RefRoot);
			NetPosition = playerInfo.position;
			NetRotation = Quaternion.identity;
			BodyColor = playerInfo.bodyColor;
			isLocalPlayer = false;
		}


		public void SetMoveInput(Vector2 moveInput)
		{
			this.moveInput = moveInput;
		}

		public void Jump()
		{
			jumpInput = true;
		}

		private void Update()
		{
			if (isLocalPlayer)
			{
				capsuleCache.RefreshShapeParams();
				// check and do jump
				// step 1: resolve penetrate
				ResolvePenetrate();
				//if (overlapResolved)
				{
					// step 2: check ground, change move mode
					PerformMove();
				}
				ClearJumpInput();
			}
		}

		private void ResolvePenetrate()
		{
			Vector3 p1 = capsuleCache.PointP1;
			Vector3 p2 = capsuleCache.PointP2;
			float radius = capsuleCache.Radius;
			int resolvePenetrateCount = 0;
			overlapResolved = false;
			Vector3 totalSeparateOffset = Vector3.zero;
			Vector3 tempCheckPos = capsuleCache.Capsule.transform.position;
			Quaternion tempCheckRot = capsuleCache.Capsule.transform.rotation;
			while (resolvePenetrateCount++ < MAX_RESOLVE_OVERLAP_COUNT)
			{
				Vector3 checkP1 = p1 + totalSeparateOffset;
				Vector3 checkP2 = p2 + totalSeparateOffset;
				int overlapCount = Physics.OverlapCapsuleNonAlloc(checkP1, checkP2, radius, penetrateOverlapCache, groundMask, QueryTriggerInteraction.Ignore);
				if (overlapCount > 0)
				{
					for (int i = 0; i < overlapCount; i++)
					{
						Collider other = penetrateOverlapCache[i];
						if (Physics.ComputePenetration(capsuleCache.Capsule, tempCheckPos, tempCheckRot,
							other, other.transform.position, other.transform.rotation,
							out Vector3 direction, out float distance))
						{
							Vector3 separateOffset = direction * (distance + AVOID_COLLIDER_DIST);
							tempCheckPos += separateOffset;
							totalSeparateOffset += separateOffset;
						}
					}
				}
				else
				{
					overlapResolved = true;
					break;
				}
			}
			transform.position += totalSeparateOffset;
		}

		private void PerformMove()
		{
			switch (currentMoveMode)
			{
				case MoveMode.Walking:
					PerformWalking();
					break;
				case MoveMode.Falling:
					PerformFalling();
					break;
				default:
					break;
			}
		}

		private void PerformWalking()
		{
			// TODO: split delta time for
			// TODO: acceleration and brake deceleration
			// add input acceleration and ramp velocity
			// move along ground
			// if not on ground, switch to falling
			// should have near ground falling?

			Vector3 rightAxis = Vector3.ProjectOnPlane(inputSpace.right, Vector3.up);
			Vector3 forwardAxis = Vector3.ProjectOnPlane(inputSpace.forward, Vector3.up);
			Vector3 playerMoveVector = rightAxis * moveInput.x + forwardAxis * moveInput.y;

			currentVelocity = maxSpeed * playerMoveVector;

			if (playerMoveVector != Vector3.zero)
			{
				Quaternion targetRotation = Quaternion.LookRotation(playerMoveVector);
				transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * rotateAngleSpeed);
			}

			var currentHorizonVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
			var floorDotVel = Vector3.Dot(groundNormal, currentHorizonVelocity);
			var rampVelocity = new Vector3(currentVelocity.x,  -floorDotVel / groundNormal.y, currentVelocity.z);

			currentVelocity = rampVelocity.normalized * currentVelocity.magnitude;
			Vector3 canMoveDelta = SafeMove(currentVelocity * Time.deltaTime);
			transform.position += canMoveDelta;
			UpdateGround();

			SendMoveMsg();
		}

		private void PerformFalling()
		{
			// add input acceleration and gravity
			// sweep and slide, move and check ground (with additive check distance)
			// if on ground, switch to walking
		}

		private Vector3 SafeMove(Vector3 delta)
		{
			Vector3 canMoveDelta = Vector3.zero;
			Vector3 dir = delta.normalized;
			float remainDist = delta.magnitude;
			int moveCount = 0;
			Vector3 p1 = capsuleCache.PointP1;
			Vector3 p2 = capsuleCache.PointP2;
			float radius = capsuleCache.Radius;
			RaycastHit hit;

			while (remainDist > 0 && moveCount++ < 3)
			{
				if (Physics.CapsuleCast(p1 + canMoveDelta, p2 + canMoveDelta, radius, dir, out hit, remainDist + AVOID_COLLIDER_DIST, groundMask, QueryTriggerInteraction.Ignore))
				{
					float moveDist = Mathf.Min(hit.distance, remainDist);
					canMoveDelta += dir * moveDist;
					remainDist -= moveDist;
					// TODO: walkable normal
					Vector3 reflectNormal = hit.normal;
					if (reflectNormal.y < 0.7)
					{
						reflectNormal.y = 0;
						reflectNormal.Normalize();
					}
					dir = Vector3.ProjectOnPlane(dir, reflectNormal);
				}
				else
				{
					canMoveDelta += dir * remainDist;
					remainDist = 0;
				}
			}
			return canMoveDelta;
		}

		private void UpdateGround()
		{
			Vector3 p1 = capsuleCache.PointP1;
			Vector3 p2 = capsuleCache.PointP2;
			float radius = capsuleCache.Radius;
			if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, maxStairHeight, groundMask, QueryTriggerInteraction.Ignore))
			{
				groundNormal = hit.normal;
			}
		}

		private void LocalPlayerUpdate()
		{
			Vector3 rightAxis = Vector3.ProjectOnPlane(inputSpace.right, Vector3.up);
			Vector3 forwardAxis = Vector3.ProjectOnPlane(inputSpace.forward, Vector3.up);
			Vector3 playerMoveVector = rightAxis * moveInput.x + forwardAxis * moveInput.y;

			currentVelocity = maxSpeed * playerMoveVector;

			NetPosition += currentVelocity * Time.deltaTime;

			if (playerMoveVector != Vector3.zero)
			{
				Quaternion targetRotation = Quaternion.LookRotation(playerMoveVector);
				NetRotation = Quaternion.RotateTowards(NetRotation, targetRotation, Time.deltaTime * rotateAngleSpeed);
			}

			float checkGroundOffset = 1f;
			Vector3 p1 = capsuleCache.PointP1 + Vector3.up * checkGroundOffset;
			Vector3 p2 = capsuleCache.PointP2 + Vector3.up * checkGroundOffset;
			float radius = capsuleCache.Radius;
			float groundDistance = float.MaxValue;
			if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
			{
				groundDistance = hit.distance - checkGroundOffset;
			}

			if (groundDistance > 0.01f)
			{
				float maxVerticalMove = Mathf.Min(5f * Time.deltaTime, groundDistance);
				NetPosition += Vector3.down * maxVerticalMove;
			}



			SendMoveMsg();
		}

		private void ClearJumpInput()
		{
			jumpInput = false;
		}

		private void SendMoveMsg()
		{
			LocalPlayerMoveMsg msg = new LocalPlayerMoveMsg()
			{
				sequence = ++sequence,
				id = id,
				position = NetPosition,
				rotation = NetRotation,
			};
			client.SendMoveMsg(msg);
		}

		public void RemotePlayerMove(RemotePlayerMoveMsg remoteMoveMsg)
		{
			if (sequence < remoteMoveMsg.sequence)
			{
				NetPosition = remoteMoveMsg.position;
				NetRotation = remoteMoveMsg.rotation;
				sequence = remoteMoveMsg.sequence;
			}
		}
	}
}
