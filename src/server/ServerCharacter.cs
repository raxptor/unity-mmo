using System;
using netki;

namespace UnityMMO
{
	public interface Controller
	{
		void ControlMe(ServerCharacter character);
	}

	public struct Vector3
	{
		public float x, y, z;

		public Vector3(float _x, float _y, float _z)
		{
			x = _x;
			y = _y;
			z = _z;
		}
	}

	public class ServerCharacterData
	{
		public uint Id;
		public bool HumanControllable;
		public Vector3 StartPosition;
	}

	public class ServerCharacter
	{
		// locations
		public ServerCharacterData Data;
		public WorldServer World;
		public Vector3 Position;
		public Vector3 Velocity;
		public float Heading = 0;
		public bool Spawned = false;
		public string CharacterTypeId;

		// controller
		public Controller Controller;
		private static Random _r = new Random();

		public ServerCharacter(ServerCharacterData data)
		{
			Data = data;
			ResetFromData(data);
		}

		public void ResetFromData(ServerCharacterData data)
		{
			Spawned = false;
		}

		public virtual void Update(float dt)
		{
			if (!Spawned)
			{
				return;
			}
		}

		public virtual void WriteFullState(Bitstream.Buffer stream)
		{
			// which character it is.
			Bitstream.PutStringDumb(stream, CharacterTypeId);
		}

		public virtual bool WriteReliableUpdate(Bitstream.Buffer stream)
		{
			return false;
		}

		public virtual bool WriteUnreliableUpdate(Bitstream.Buffer stream)
		{
			Bitstream.PutBits(stream, 32, (uint)_r.Next());
			return true;
		}
	}
}
