using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NetMoveSimulate
{
	public sealed class CapsuleParamsCache
	{
		private Vector3 _transformScale;
		private int _capsuleDirection;
		private Vector3 _capsuleRawCenter;
		private float _capsuleRawHeight;
		private float _capsuleRawRadius;

		private float _capsuleHeight;
		private float _capsuleRadius;
		// _localCenter, _localP1, _localP2 without position and rotation of capsule's transform
		private Vector3 _localCenter;
		private Vector3 _localP1;
		private Vector3 _localP2;
		private CapsuleCollider _capsule;
		private Transform _transform;

		public CapsuleCollider Capsule => _capsule;
		public Transform Transform => _transform;
		public float Radius => _capsuleRadius;
		public Vector3 LocalPointP1 => _localP1;
		public Vector3 LocalPointP2 => _localP2;
		public Vector3 PointP1 => _transform.position + _transform.rotation * _localP1;
		public Vector3 PointP2 => _transform.position + _transform.rotation * _localP2;

		public CapsuleParamsCache(CapsuleCollider capsule)
		{
			_transform = capsule.transform;
			_capsule = capsule;
			CalcShapeParams();
		}

		public void RefreshShapeParams()
		{
			bool cacheValid = true;
			cacheValid &= _transformScale == _transform.lossyScale;
			cacheValid &= _capsuleDirection == _capsule.direction;
			cacheValid &= _capsuleRawCenter == _capsule.center;
			cacheValid &= _capsuleRawHeight == _capsule.height;
			cacheValid &= _capsuleRawRadius == _capsule.radius;
			if (!cacheValid)
			{
				CalcShapeParams();
			}
		}

		private void CalcShapeParams()
		{
			_transformScale = _transform.lossyScale;
			_capsuleDirection = _capsule.direction;
			_capsuleRawCenter = _capsule.center;
			_capsuleRawHeight = _capsule.height;
			_capsuleRawRadius = _capsule.radius;

			var absScale = _transformScale;
			absScale.x = Mathf.Abs(absScale.x);
			absScale.y = Mathf.Abs(absScale.y);
			absScale.z = Mathf.Abs(absScale.z);
			float radius;
			float halfHeight;
			Vector3 upOffset;
			switch (_capsule.direction)
			{
				// x axis
				case 0:
					radius = _capsuleRawRadius * Mathf.Max(absScale.y, absScale.z);
					halfHeight = Mathf.Max(_capsuleRawHeight * 0.5f * absScale.x, radius);
					upOffset = Vector3.right * (halfHeight - radius);
					break;
				// y axis
				case 1:
					radius = _capsuleRawRadius * Mathf.Max(absScale.x, absScale.z);
					halfHeight = Mathf.Max(_capsuleRawHeight * 0.5f * absScale.y, radius);
					upOffset = Vector3.up * (halfHeight - radius);
					break;
				// z axis
				case 2:
					radius = _capsuleRawRadius * Mathf.Max(absScale.x, absScale.y);
					halfHeight = Mathf.Max(_capsuleRawHeight * 0.5f * absScale.z, radius);
					upOffset = Vector3.forward * (halfHeight - radius);
					break;
				// fallback y axis
				default:
					radius = _capsuleRawRadius * Mathf.Max(absScale.y, absScale.z);
					halfHeight = Mathf.Max(_capsuleRawHeight * 0.5f * absScale.x, radius);
					upOffset = Vector3.right * (halfHeight - radius);
					break;
			}

			_capsuleHeight = halfHeight * 2f;
			_capsuleRadius = radius;
			_localCenter = Vector3.Scale(_capsuleRawCenter, _transformScale);
			_localP1 = _localCenter + upOffset;
			_localP2 = _localCenter - upOffset;
		}
	}

}
