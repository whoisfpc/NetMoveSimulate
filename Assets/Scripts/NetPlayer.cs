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

		private const float CHECK_GROUND_UP_OFFSET = 0.1f;
		private const float GROUND_FLOAT_MAX_DIST = 0.023f;
		private const float GROUND_FLOAT_MIN_DIST = 0.015f;
		private const float AVOID_COLLIDER_DIST = 0.01f;
		private const int MAX_RESOLVE_OVERLAP_COUNT = 3;

		[Header("MoveSettings")]
		public float maxSpeed = 5f;
		public float groundAcceleration = 10f;
		public float groundDeceleration = 20f;
		public float airAcceleration = 5f;
		public float airDeceleration = 0f;
		public float rotateAngleSpeed = 360f;
		public LayerMask groundMask;
		public float maxStairHeight = 0.3f;
		public float minGroundNormal = 0.707f;
		public float jumpInitSpeed = 20f;

		[Header("PlayerInfos")]
		public Renderer bodyRenderer;
		public uint id;
		public CapsuleCollider capsule;

		private Vector2 moveInput;
		private bool jumpInput;
		private Transform inputSpace;
		private bool isLocalPlayer;
		private NetClient client;
		private uint sequence;

		private readonly Collider[] penetrateOverlapCache = new Collider[64];
		// TODO: add snap ground speed
		private Vector3 gravity = new Vector3(0, -9.8f, 0);
		private Vector3 currentVelocity;
		private MoveMode currentMoveMode = MoveMode.Walking;
		private bool overlapResolved;
		private CapsuleParamsCache capsuleCache;
		private Vector3 groundNormal = Vector3.up;
		private float groundDist = 0;
		// update every frame by CachePlayerMoveVector
		private Vector3 playerMoveVector;

		private void Awake()
		{
			capsuleCache = new CapsuleParamsCache(transform, capsule);
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
			this.moveInput = moveInput.normalized;
		}

		public void Jump()
		{
			jumpInput = true;
		}

		private void CachePlayerMoveVector()
		{
			Vector3 rightAxis = Vector3.ProjectOnPlane(inputSpace.right, Vector3.up);
			Vector3 forwardAxis = Vector3.ProjectOnPlane(inputSpace.forward, Vector3.up);
			playerMoveVector = rightAxis * moveInput.x + forwardAxis * moveInput.y;
		}

		private void Update()
		{
			if (isLocalPlayer)
			{
				float dt = Time.deltaTime;

				CachePlayerMoveVector();
				capsuleCache.RefreshShapeParams();
				// step 1: resolve penetrate
				ResolvePenetrate();
				// check and do jump
				CheckAndTryJump();
				//if (overlapResolved)
				{
					// step 2: check ground, change move mode
					PerformMove(dt);
				}
				PerformRotate(dt);
				ClearJumpInput();
				SendMoveMsg();
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

		private void PerformMove(float dt)
		{
			switch (currentMoveMode)
			{
				case MoveMode.Walking:
					PerformWalking(dt);
					break;
				case MoveMode.Falling:
					PerformFalling(dt);
					break;
				default:
					break;
			}
			Debug.DrawRay(transform.position + Vector3.up, currentVelocity, Color.blue, 0, false);
		}

		private void PerformRotate(float dt)
		{
			if (playerMoveVector != Vector3.zero)
			{
				Quaternion targetRotation = Quaternion.LookRotation(playerMoveVector);
				transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, dt * rotateAngleSpeed);
			}
		}

		private void PerformWalking(float dt)
		{
			// TODO: split delta time for
			// TODO: acceleration and brake deceleration
			// TODO: deal with step up
			// add input acceleration and ramp velocity
			// move along ground
			// if not on ground, switch to falling
			// should have near ground falling?

			if (playerMoveVector == Vector3.zero)
			{
				currentVelocity = Vector3.MoveTowards(currentVelocity, Vector3.zero, groundDeceleration * dt);
			}
			else
			{
				float effectiveAcceleration = groundAcceleration;
				if (groundDeceleration > groundAcceleration && Vector3.Dot(playerMoveVector, currentVelocity) <= 0)
				{
					effectiveAcceleration = groundDeceleration;
				}
				var moveVectorOnRamp = CalcRampVector(playerMoveVector, groundNormal).normalized;
				currentVelocity = Vector3.ClampMagnitude(currentVelocity + dt * effectiveAcceleration * moveVectorOnRamp, maxSpeed);
			}

			var rampVector = CalcRampVector(currentVelocity, groundNormal);

			currentVelocity = rampVector.normalized * currentVelocity.magnitude;
			if (groundDist > GROUND_FLOAT_MAX_DIST)
			{
				currentVelocity.y += gravity.y * dt;
			}
			Vector3 safeMoveEndPos = SafeMove(currentVelocity * dt);
			transform.position = safeMoveEndPos;
			UpdateGround();
		}

		private Vector3 CalcRampVector(Vector3 v, Vector3 rampNormal)
		{
			if (rampNormal.y <= 0 || rampNormal.y > 1)
			{
				return v;
			}
			var vHorizon = new Vector3(v.x, 0, v.z);
			var floorDotVel = Vector3.Dot(rampNormal, vHorizon);
			var rampVector = new Vector3(vHorizon.x, -floorDotVel / rampNormal.y, vHorizon.z);
			return rampVector;
		}

		private void PerformFalling(float dt)
		{
			// add input acceleration and gravity
			// sweep and slide, move and check ground (with additive check distance)
			// if on ground, switch to walking
			var currentHorizonVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
			var verticalSpeed = currentVelocity.y;
			if (playerMoveVector == Vector3.zero)
			{
				currentHorizonVelocity = Vector3.MoveTowards(currentHorizonVelocity, Vector3.zero, airDeceleration * dt);
			}
			else
			{
				float effectiveAcceleration = airAcceleration;
				if (airDeceleration > airAcceleration && Vector3.Dot(playerMoveVector, currentHorizonVelocity) <= 0)
				{
					effectiveAcceleration = airDeceleration;
				}
				currentHorizonVelocity = Vector3.ClampMagnitude(currentHorizonVelocity + dt * effectiveAcceleration * playerMoveVector, maxSpeed);
			}
			verticalSpeed += gravity.y * dt;
			currentVelocity = currentHorizonVelocity + Vector3.up * verticalSpeed;
			Vector3 safeMoveEndPos = SafeMove(currentVelocity * dt);
			transform.position = safeMoveEndPos;
			UpdateGround();
		}

		private Vector3 SafeMove(Vector3 delta)
		{
			// TODO: a lot of error
			Vector3 tempPos = transform.position;
			Vector3 dir = delta.normalized;
			float remainDist = delta.magnitude;
			int moveCount = 0;
			Vector3 p1Offset = capsuleCache.PointP1 - tempPos;
			Vector3 p2Offset = capsuleCache.PointP2 - tempPos;
			float radius = capsuleCache.Radius;
			RaycastHit hit;

			while (remainDist > 0 && moveCount++ < 3)
			{
				if (Physics.CapsuleCast(p1Offset + tempPos, p2Offset + tempPos, radius, dir, out hit, remainDist + AVOID_COLLIDER_DIST, groundMask, QueryTriggerInteraction.Ignore))
				{
					float moveDist = Mathf.Min(hit.distance - AVOID_COLLIDER_DIST, remainDist);
					tempPos += dir * moveDist + hit.normal * AVOID_COLLIDER_DIST;
					remainDist -= moveDist;
					Vector3 reflectNormal = hit.normal;
					// find impact normal
					Ray impactCheckRay = new Ray(hit.point + 0.1f * hit.normal, -hit.normal);
					if (hit.collider.Raycast(impactCheckRay, out RaycastHit rayHit, 0.2f))
					{
						reflectNormal = rayHit.normal;
					}
					Debug.DrawRay(hit.point, reflectNormal, Color.red, 0, false);
					if (currentMoveMode == MoveMode.Walking)
					{
						if (reflectNormal.y < minGroundNormal)
						{
							if (Vector3.Dot(reflectNormal, dir) < 0 && hit.point.y <= tempPos.y + maxStairHeight)
							{
								if (TryStepUp(tempPos, dir, ref remainDist, out Vector3 stepGroundPos))
								{
									tempPos = stepGroundPos;
									continue;
								}
							}
							reflectNormal.y = 0;
							reflectNormal.Normalize();
							var speedScale = Vector3.ProjectOnPlane(dir, reflectNormal).magnitude;
							remainDist *= speedScale;
							dir = Vector3.ProjectOnPlane(dir, reflectNormal).normalized;
							//currentVelocity = currentVelocity.magnitude * speedScale * dir;
						}
						else
						{
							dir = CalcRampVector(dir, reflectNormal).normalized;
							//currentVelocity = currentVelocity.magnitude * dir;
						}
					}
					else
					{
						var speedScale = Vector3.ProjectOnPlane(dir, reflectNormal).magnitude;
						remainDist *= speedScale;
						dir = Vector3.ProjectOnPlane(dir, reflectNormal).normalized;
						//currentVelocity = currentVelocity.magnitude * speedScale * dir;
					}
				}
				else
				{
					tempPos += dir * remainDist;
					remainDist = 0;
				}
			}
			return tempPos;
		}

		private bool TryStepUp(Vector3 oldPos, Vector3 moveDir, ref float remainDist, out Vector3 stepGroundPos)
		{
			Vector3 p1 = capsuleCache.GetPoint1WithRootPos(oldPos);
			Vector3 p2 = capsuleCache.GetPoint2WithRootPos(oldPos);
			float radius = capsuleCache.Radius;
			Vector3 stepUpOffset = Vector3.up * maxStairHeight;
			RaycastHit hit;
			if (Physics.CapsuleCast(p1, p2, radius, Vector3.up, out hit, maxStairHeight, groundMask, QueryTriggerInteraction.Ignore))
			{
				stepUpOffset = Vector3.up * Mathf.Max(0, hit.distance - AVOID_COLLIDER_DIST);
			}
			float forwardDist = remainDist + 0.05f;
			if (!Physics.CapsuleCast(p1 + stepUpOffset, p2 + stepUpOffset, radius, moveDir, out hit, forwardDist, groundMask, QueryTriggerInteraction.Ignore))
			{
				Vector3 stepProbePos = oldPos + stepUpOffset + moveDir * forwardDist;
				if (FindGround(stepProbePos, stepUpOffset.y + maxStairHeight, out float downGroundDist))
				{
					remainDist -= forwardDist;
					stepGroundPos = stepProbePos + Vector3.down * downGroundDist;
					return true;
				}
			}
			stepGroundPos = oldPos;
			return false;
		}

		private bool FindGround(Vector3 pos, float maxProbeGroundDist, out float downGroundDist)
		{
			Vector3 upOffset = Vector3.up * CHECK_GROUND_UP_OFFSET;
			Vector3 p1 = capsuleCache.GetPoint1WithRootPos(pos) + upOffset;
			Vector3 p2 = capsuleCache.GetPoint2WithRootPos(pos) + upOffset;
			float radius = capsuleCache.Radius;
			float checkDist = maxProbeGroundDist + CHECK_GROUND_UP_OFFSET + GROUND_FLOAT_MAX_DIST;
			bool foundGround = false;
			downGroundDist = 0;
			if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, checkDist, groundMask, QueryTriggerInteraction.Ignore))
			{
				float dist = hit.distance - CHECK_GROUND_UP_OFFSET;
				// TODO: add gravity for walking mode
				foundGround = IsValidGroundHit(hit);
				if (!foundGround)
				{
					Ray impactCheckRay = new Ray(hit.point + upOffset, Vector3.down);
					if (hit.collider.Raycast(impactCheckRay, out RaycastHit rayHit, CHECK_GROUND_UP_OFFSET * 2f))
					{
						foundGround = IsValidGroundHit(rayHit);
					}
				}
				if (foundGround)
				{
					downGroundDist = dist;
				}
			}
			return foundGround;
		}

		private void UpdateGround()
		{
			Vector3 upOffset = Vector3.up * CHECK_GROUND_UP_OFFSET;
			Vector3 p1 = capsuleCache.PointP1 + upOffset;
			Vector3 p2 = capsuleCache.PointP2 + upOffset;
			float radius = capsuleCache.Radius;
			float checkDist = maxStairHeight + CHECK_GROUND_UP_OFFSET + GROUND_FLOAT_MAX_DIST;
			bool foundGround = false;
			if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, checkDist, groundMask, QueryTriggerInteraction.Ignore))
			{
				// TODO: check distance by move mode
				float dist = hit.distance - CHECK_GROUND_UP_OFFSET;
				if (currentMoveMode == MoveMode.Falling)
				{
					if (dist <= GROUND_FLOAT_MAX_DIST)
					{
						foundGround = IsValidGroundHit(hit);
					}
				}
				else
				{
					// TODO: add gravity for walking mode
					foundGround = IsValidGroundHit(hit);
					if (!foundGround)
					{
						if (Physics.Raycast(transform.position + upOffset, Vector3.down, out RaycastHit rayHit, checkDist + GROUND_FLOAT_MAX_DIST, groundMask, QueryTriggerInteraction.Ignore))
						{
							foundGround = IsValidGroundHit(rayHit);
						}
					}
				}
				if (foundGround)
				{
					groundNormal = hit.normal;
					groundDist = dist;
				}
			}

			if (!foundGround)
			{
				SwitchMoveMode(MoveMode.Falling);
			}
			else if (currentVelocity.y <= 0)
			{
				SwitchMoveMode(MoveMode.Walking);
			}
		}

		private bool IsValidGroundHit(RaycastHit hit)
		{
			return hit.normal.y >= minGroundNormal;
		}

		private void CheckAndTryJump()
		{
			if (jumpInput && currentMoveMode == MoveMode.Walking)
			{
				currentVelocity.y = Mathf.Max(currentVelocity.y + jumpInitSpeed, jumpInitSpeed);
				SwitchMoveMode(MoveMode.Falling);
			}
		}

		private void ClearJumpInput()
		{
			jumpInput = false;
		}

		private void SwitchMoveMode(MoveMode newMode)
		{
			if (currentMoveMode != newMode)
			{
				currentMoveMode = newMode;
				if (currentMoveMode == MoveMode.Walking)
				{
					currentVelocity.y = 0;
				}
			}
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
