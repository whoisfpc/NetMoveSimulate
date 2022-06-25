using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NetMoveSimulate.Network
{
	public class PlayerSpawnInfo
	{
		public Vector3 position;
		public Quaternion rotation;
		public Color bodyColor;
		public uint id;
	}

	public class ConnectSuccessMsg
	{
		public List<PlayerSpawnInfo> remotePlayers;
		public uint playerId;
	}

	public class LocalPlayerMoveMsg
	{
		public uint sequence;
		public uint id;
		public Vector3 position;
		public Quaternion rotation;
	}

	public class RemotePlayerMoveMsg
	{
		public uint sequence;
		public uint id;
		public Vector3 position;
		public Quaternion rotation;
	}
}
