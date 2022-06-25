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

		private Vector2 moveInput;
		private bool jumpInput;
		private Transform inputSpace;
		private bool isLocalPlayer;
		private NetClient client;
		private uint sequence;

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
				LocalPlayerUpdate();
			}
		}

		private void LocalPlayerUpdate()
		{
			Vector3 rightAxis = Vector3.ProjectOnPlane(inputSpace.right, Vector3.up);
			Vector3 forwardAxis = Vector3.ProjectOnPlane(inputSpace.forward, Vector3.up);
			Vector3 playerMoveVector = rightAxis * moveInput.x + forwardAxis * moveInput.y;

			NetPosition += maxSpeed * Time.deltaTime * playerMoveVector;

			if (playerMoveVector != Vector3.zero)
			{
				Quaternion targetRotation = Quaternion.LookRotation(playerMoveVector);
				NetRotation = Quaternion.RotateTowards(NetRotation, targetRotation, Time.deltaTime * rotateAngleSpeed);
			}

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
