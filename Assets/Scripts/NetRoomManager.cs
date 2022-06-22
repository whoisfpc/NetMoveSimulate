using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetMoveSimulate
{
	public class NetRoomManager : MonoBehaviour
	{
		public NetServer server;

		public Camera mainCamera;

		public NetClient clientPrefab;

		public float clientXOffset = 100f;

		public Vector2 lagRange;
		public Vector2 lagVarianceRange;
		public Vector2 lossRange;

		private PlayerInputManager playerInputManager;


		void Awake()
		{
			playerInputManager = GetComponent<PlayerInputManager>();
		}

		void OnPlayerJoined(PlayerInput player)
		{
			player.gameObject.name = $"VirtualNetUser {player.user.id}";
			var xPos = player.user.id * clientXOffset;
			GameObject clientRefRoot = new GameObject($"Client {player.user.id}");
			clientRefRoot.transform.position = new Vector3(xPos, 0, 0);
			var client = Instantiate(clientPrefab);
			client.lag = Random.Range(lagRange.x, lagRange.y);
			client.lagVariance = Random.Range(lagVarianceRange.x, lagVarianceRange.y);
			client.loss = Random.Range(lossRange.x, lossRange.y);
			client.Init(clientRefRoot.transform, player, server);
			
			//mainCamera.gameObject.SetActive(false);
		}	

		void OnPlayerLeft(PlayerInput player)
		{
			//if (playerInputManager.playerCount == 0)
			//{
			//	mainCamera.gameObject.SetActive(true);
			//}
		}
	}
}