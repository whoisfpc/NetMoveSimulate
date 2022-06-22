using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using NetMoveSimulate.Network;

namespace NetMoveSimulate
{
	public class NetClient : MonoBehaviour
	{
		public GameObject playerPrefab;

		public Transform spawnPoint;
		[Range(0, 1)]
		public float lag = 0f;
		[Range(0, 1)]
		public float lagVariance = 0f;
		[Range(0, 1)]
		public float loss = 0f;

		public Transform RefRoot { get; private set; }

		public NetPlayer LocalPlayer { get; private set; }

		private NetUser user;

		private PlayerInput playerInput;
		private bool isConnected;
		private NetChannel receiveChannel;
		private NetChannel sendChannel;
		private List<NetPlayer> players = new();

		private float oldLag, oldLagVariance, oldLoss;

		public void Init(Transform refRoot, PlayerInput playerInput, NetServer server)
		{
			Debug.Assert(refRoot != null, "NetClient refRoot is null!");
			RefRoot = refRoot;
			transform.SetParent(refRoot);
			transform.localPosition = Vector3.zero;
			transform.localRotation = Quaternion.identity;

			this.playerInput = playerInput;
			user = playerInput.GetComponent<NetUser>();

			var playerObj = Instantiate(playerPrefab);
			var player = playerObj.GetComponent<NetPlayer>();
			player.InitLocal(this, playerInput.camera.transform);
			players.Add(player);
			LocalPlayer = player;
			user.Player = player;
			user.OrbitCamera = playerInput.camera.GetComponent<OrbitCamera>();
			user.OrbitCamera.focus = player.transform;
			
			server.Connect(this, out sendChannel, out receiveChannel);
			isConnected = true;
			TryRefreshNetInfoText(true);
		}

		private void Update()
		{
			if (isConnected)
			{
				// TODO: fetch interval
				FetchServerMessage();
			}
		}

		private void OnValidate()
		{
			TryRefreshNetInfoText(false);
		}

		private void TryRefreshNetInfoText(bool force)
		{
			bool hasDiff = false;
			hasDiff |= oldLag != lag;
			hasDiff |= oldLagVariance != lagVariance;
			hasDiff |= oldLoss != loss;
			if (hasDiff || force)
			{
				oldLag = lag;
				oldLagVariance = lagVariance;
				oldLoss = loss;

				float lagMs = lag * 1000;
				float lagVarianceMs = lagVariance * 1000;
				string infoText = $"Lag: {lagMs:F0}ms\nLagVarianse: {lagVarianceMs:F0}ms\nLoss: {loss:P1}%";
				user.netInfoText.text = infoText;
			}
		}

		private void FetchServerMessage()
		{
			object msg = receiveChannel.Fetch(Time.time);
			while (msg != null)
			{
				switch (msg)
				{
					case ConnectSuccessMsg connectSucces:
						OnConnectSuccess(connectSucces);
						break;
					case PlayerSpawnInfo playerInfo:
						OnRemoteSpawnPlayer(playerInfo);
						break;
					case RemotePlayerMoveMsg remoteMoveMsg:
						OnRemotePlayerMove(remoteMoveMsg);
						break;
					default:
						break;
				}
				msg = receiveChannel.Fetch(Time.time);
			}
		}

		private void OnConnectSuccess(ConnectSuccessMsg msg)
		{
			LocalPlayer.id = msg.playerId;
			foreach (var info in msg.remotePlayers)
			{
				var playerObj = Instantiate(playerPrefab);
				var player = playerObj.GetComponent<NetPlayer>();
				player.InitRemote(this, info);
				players.Add(player);
			}
		}

		private void OnRemoteSpawnPlayer(PlayerSpawnInfo playerInfo)
		{
			var playerObj = Instantiate(playerPrefab);
			var player = playerObj.GetComponent<NetPlayer>();
			player.InitRemote(this, playerInfo);
			players.Add(player);
		}

		private void OnRemotePlayerMove(RemotePlayerMoveMsg remoteMoveMsg)
		{
			foreach (var player in players)
			{
				if (player.id == remoteMoveMsg.id)
				{
					player.RemotePlayerMove(remoteMoveMsg);
					break;
				}
			}
		}

		public void SendMoveMsg(LocalPlayerMoveMsg msg)
		{
			// TODO: send delay queue
			if (isConnected)
			{
				sendChannel.Push(Time.time, msg);
			}
		}
	}
}
