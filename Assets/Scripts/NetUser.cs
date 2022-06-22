using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace NetMoveSimulate
{
	public class NetUser : MonoBehaviour
	{
		public Text netInfoText;
		public NetPlayer Player { get; set; }
		public OrbitCamera OrbitCamera { get; set; }

		private void OnMove(InputValue value)
		{
			if (!Player) return;
			Player.SetMoveInput(value.Get<Vector2>());
		}

		private void OnLook(InputValue value)
		{
			if (!OrbitCamera) return;
			OrbitCamera.SetRotateInput(value.Get<Vector2>());
		}

		private void OnJump()
		{
			if (!Player) return;
			Player.Jump();
		}
	}
}
