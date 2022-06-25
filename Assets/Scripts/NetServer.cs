using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NetMoveSimulate.Network;

namespace NetMoveSimulate
{
	public class NetServer : MonoBehaviour
	{
		private class NetPlayerInfo
		{
			public readonly uint id;
			public readonly Color bodyColor;
			public Vector3 position;
			public Quaternion rotation;
			public uint maxSequence;

			public NetPlayerInfo(uint id, Color bodyColor)
			{
				this.id = id;
				this.bodyColor = bodyColor;
			}
		}

		private readonly List<NetChannel> receiveChannels = new();
		private readonly List<NetChannel> sendChannels = new();
		private readonly List<NetPlayerInfo> playerInfos = new();
		
		private uint idSeed = 1;
		private uint GenerateId() => idSeed++;

		void Start()
		{
		
		}

		void Update()
		{
			// TODO: fetch and send interval
			FetchRemoteMessages();
			SendRemoteMessages();
		}

		private void FetchRemoteMessages()
		{
			foreach (var receiveChannel in receiveChannels)
			{
				object msg = receiveChannel.Fetch(Time.time);
				while (msg != null)
				{
					switch (msg)
					{
						case LocalPlayerMoveMsg moveMsg:
							OnPlayerMoveMsg(moveMsg);
							break;
						default:
							break;
					}
					msg = receiveChannel.Fetch(Time.time);
				}
			}
		}

		private void OnPlayerMoveMsg(LocalPlayerMoveMsg moveMsg)
		{
			foreach (var info in playerInfos)
			{
				if (info.id == moveMsg.id)
				{
					if (info.maxSequence < moveMsg.sequence)
					{
						info.position = moveMsg.position;
						info.rotation = moveMsg.rotation;
						info.maxSequence = moveMsg.sequence;
					}
					break;
				}
			}
		}

		private void SendRemoteMessages()
		{
			foreach (var info in playerInfos)
			{
				var moveMsg = new RemotePlayerMoveMsg()
				{
					id = info.id,
					sequence = info.maxSequence,
					position = info.position,
					rotation = info.rotation,
				};
				foreach (var sendChannel in sendChannels)
				{
					if (sendChannel.id != moveMsg.id)
					{
						sendChannel.Push(Time.time, moveMsg);
					}
				}
			}
		}

		public void Connect(NetClient client, out NetChannel c2sChannel, out NetChannel s2cChannel)
		{
			uint id = GenerateId();
			c2sChannel = new NetChannel(id, client.lag, client.lagVariance, client.loss);
			s2cChannel = new NetChannel(id, client.lag, client.lagVariance, client.loss);

			ConnectSuccessMsg msg = new ConnectSuccessMsg();
			msg.playerId = id;
			msg.remotePlayers = new List<PlayerSpawnInfo>();
			foreach (var info in playerInfos)
			{
				var remoteInfo = new PlayerSpawnInfo()
				{
					id = info.id,
					position = info.position,
					rotation = info.rotation,
					bodyColor = info.bodyColor,
				};
				msg.remotePlayers.Add(remoteInfo);
			}
			s2cChannel.Push(Time.time, msg);

			var newPlayerInfo = new PlayerSpawnInfo()
			{
				id = id,
				position = client.LocalPlayer.NetPosition,
				rotation = client.LocalPlayer.NetRotation,
				bodyColor = client.LocalPlayer.BodyColor,
			};
			foreach (var sendChannel in sendChannels)
			{
				sendChannel.Push(Time.time, newPlayerInfo);
			}

			NetPlayerInfo playerInfo = new NetPlayerInfo(id, client.LocalPlayer.BodyColor)
			{
				position = client.LocalPlayer.NetPosition,
				rotation = client.LocalPlayer.NetRotation
			};
			playerInfos.Add(playerInfo);
			receiveChannels.Add(c2sChannel);
			sendChannels.Add(s2cChannel);
		}
	}
}
