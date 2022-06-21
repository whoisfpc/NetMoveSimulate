using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetMoveSimulate
{
	public class NetUser : MonoBehaviour
	{
		public NetPlayer player;
		public OrbitCamera orbitCamera;

		private void OnMove(InputValue value)
		{
			if (!player) return;
			player.SetMoveInput(value.Get<Vector2>());
		}

		private void OnLook(InputValue value)
		{
			if (!orbitCamera) return;
			orbitCamera.SetRotateInput(value.Get<Vector2>());
		}

		private void OnJump()
		{
			if (!player) return;
			player.Jump();
		}
	}
}
