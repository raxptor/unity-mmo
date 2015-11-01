using System;
using netki;

namespace UnityMMO
{
	public interface Controller
	{
		void ControlMe(uint iteration, ServerCharacter character);
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
		public Vector3 DefaultSpawnPos;
	}

	public class ServerCharacter : Entity
	{
		// locations
		public ServerCharacterData Data;
		public ServerPlayer Player;
		public WorldServer World;
		public Vector3 Position;
		public Vector3 Velocity;
		public float Heading = 0;
		public bool Spawned = false;
		public string CharacterTypeId;
		public float TimeOffset = 0;
		public bool GotNew = false;

		public object ControllerData = null;

		// controller
		public Controller Controller;

		public ServerCharacter(ServerCharacterData data)
		{
			Data = data;
			ResetFromData(data);
		}

		public void ResetFromData(ServerCharacterData data)
		{
			Spawned = false;
		}

		// For testing only.
		public void MirrorIt(ServerCharacter from)
		{
			Data = from.Data;
			Position = from.Position;
			Position.z = Position.z + 4;
			Velocity = from.Velocity;
			Heading = from.Heading;
			Spawned = from.Spawned;
			CharacterTypeId = from.CharacterTypeId;
			TimeOffset = from.TimeOffset;
			GotNew = from.GotNew;
		}

		public override void Update(float dt)
		{
			if (!Spawned)
			{
				return;
			}
		}

		public override void WriteFullState(Bitstream.Buffer stream)
		{
			// which character it is.
			Bitstream.PutStringDumb(stream, CharacterTypeId);
			Bitstream.PutFloat(stream, Position.x);
			Bitstream.PutFloat(stream, Position.y);
			Bitstream.PutFloat(stream, Position.z);
			Bitstream.PutCompressedInt(stream, (int)TimeOffset);
		}

		public override bool WriteReliableUpdate(Bitstream.Buffer stream)
		{
			return false;
		}

		public override bool WriteUnreliableUpdate(Bitstream.Buffer stream)
		{
			if (GotNew)
			{
				Bitstream.PutFloat(stream, Position.x);
				Bitstream.PutFloat(stream, Position.y);
				Bitstream.PutFloat(stream, Position.z);
				Bitstream.PutFloat(stream, Velocity.x);
				Bitstream.PutFloat(stream, Velocity.y);
				Bitstream.PutFloat(stream, Velocity.z);
				Bitstream.PutCompressedInt(stream, (int)TimeOffset);
				GotNew = false;
				return true;
			}
			return false;
		}
	}
}
