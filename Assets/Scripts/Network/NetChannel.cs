using System.Collections.Generic;
using UnityEngine;

namespace NetMoveSimulate.Network
{
	public class NetChannel
	{
		private class ChannelPackage
		{
			public readonly float ValidTime;
			public readonly object Payload;
			public ChannelPackage(float validTime, object payload)
			{
				ValidTime = validTime;
				Payload = payload;
			}
		}

		// µ•œÚ—”≥Ÿ
		public float lag;
		public float lagVariance;
		public float loss;
		public readonly uint id;

		private List<ChannelPackage> queue;

		public NetChannel(uint id, float lag = 0f, float lagVariance = 0f, float loss = 0f)
		{
			this.id = id;
			this.lag = lag;
			this.lagVariance = lagVariance;
			this.loss = loss;
			queue = new List<ChannelPackage>();
		}

		public void Push(float time, object message)
		{
			if (message == null)
			{
				return;
			}
			if (Random.value >= loss)
			{
				var validTime = time + Mathf.Max(0, lag + Random.Range(-1f, 1f) * lagVariance);
				queue.Add(new ChannelPackage(validTime, message));
			}
		}

		public object Fetch(float now)
		{
			var idx = -1;
			var minTime = now;
			for (var i = 0; i < queue.Count; i++)
			{
				var item = queue[i];
				if (item.ValidTime <= minTime)
				{
					minTime = item.ValidTime;
					idx = i;
				}
			}
			if (idx == -1)
			{
				return null;
			}
			else
			{
				var payload = queue[idx].Payload;
				queue.RemoveAt(idx);
				return payload;
			}
		}
	}
}
