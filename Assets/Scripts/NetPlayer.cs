using NetMoveSimulate.Network;
using UnityEngine;

namespace NetMoveSimulate
{
	public class NetPlayer : MonoBehaviour
	{
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
		private CapsuleParamsCache capsuleCache;

		private void Awake()
		{
			capsuleCache = new CapsuleParamsCache(capsule);
		}

		public void InitLocal(NetClient client, Transform inputSpace)
		{
			this.client = client;
			RefRoot = client.RefRoot;
			transform.SetParent(RefRoot);
			var spawnNetPos = RefRoot.InverseTransformPoint(client.spawnPoint.position);
			var spawnNetRot = Quaternion.Inverse(RefRoot.rotation) * client.spawnPoint.rotation;
			NetPosition = spawnNetPos;
			NetRotation = spawnNetRot;
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
				ResolvePenetrate();
				// step 1: resolve penetrate
				// step 2: check ground, change move mode
				LocalPlayerUpdate();
				ClearJumpInput();
			}
		}

		private void ResolvePenetrate()
		{
			Vector3 p1 = capsuleCache.PointP1;
			Vector3 p2 = capsuleCache.PointP2;
			float radius = capsuleCache.Radius;
			int resolvePenetrateCount = 0;
			bool overlapResolved = false;
			Vector3 totalSeparateOffset = Vector3.zero;
			Vector3 tempCheckPos = capsuleCache.Capsule.transform.position;
			Quaternion tempCheckRot = capsuleCache.Capsule.transform.rotation;
			while (resolvePenetrateCount < 3)
			{
				resolvePenetrateCount++;
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
							Vector3 separateOffset = direction * distance;
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
			NetPosition += totalSeparateOffset;
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
