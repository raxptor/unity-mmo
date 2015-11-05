using System;
using System.Collections.Generic;
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

		public override string ToString()
		{
			return "[" + x + ":" + y + ":" + z + "]";
		}

		public override bool Equals(object obj)
		{
			if (obj is Vector3)
			{
				Vector3 k = (Vector3) obj;
				return k.x == x && k.y == y && k.z == z;
			}
			return false;			
		}

		public override int GetHashCode()
		{
			return (int)x;
		}

		public static bool operator==(Vector3 a, Vector3 b)
		{
			return a.x == b.x && a.y == b.y && a.z == b.z;
		}
		public static bool operator!=(Vector3 a, Vector3 b)
		{
			return a.x != b.x || a.y != b.y || a.z != b.z;
		}
		public static Vector3 operator-(Vector3 a, Vector3 b)
		{
			return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
		}
		public static Vector3 operator+(Vector3 a, Vector3 b)
		{
			return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
		}
		public static Vector3 operator*(float k, Vector3 a)
		{
			return new Vector3(k * a.x, k * a.y, k * a.z);
		}
		public static Vector3 CompMul(Vector3 a, Vector3 b)
		{
			return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
		}
		public static float Dot(Vector3 a, Vector3 b)
		{
			return a.x * b.x + a.y * b.y + a.z * b.z;
		}
		public static Vector3 Cross(Vector3 v1, Vector3 v2)
		{
			float x, y, z;
			x = v1.y * v2.z - v2.y * v1.z;
			y = (v1.x * v2.z - v2.x * v1.z) * -1;
			z = v1.x * v2.y - v2.x * v1.y;
			return new Vector3(x, y, z);
		}
	}

	public class ServerCharacterData
	{
		public uint Id;
		public bool HumanControllable;
		public Vector3 StartPosition;
		public Vector3 DefaultSpawnPos;
		public float Radius = 0.5f;
		public uint HP;
	}

	public class ServerCharacter : Entity
	{
		// locations
		public ServerCharacterData Data;
		public ServerPlayer Player;
		public WorldServer World;

		public bool Spawned = false;

		public Vector3 Position;
		public Vector3 Velocity;
		public float Heading = 0;

		public uint HP, MaxHP;

		public List<ServerPlayer.ItemInstance> Equipped = new List<ServerPlayer.ItemInstance>(); 
		public bool SendNewEquip = false;

		public string CharacterTypeId;
		public float TimeOffset = 0;

		public bool GotNew = false;

		public static Vector3 HeadingVector(float heading)
		{
			return new Vector3(
				(float)Math.Sin(heading),
				0,
				(float)Math.Cos(heading)
			);
		}

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
			HP = MaxHP = data.HP;
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

		public override void Update(uint iteration, float dt)
		{
			if (!Spawned)
			{
				return;
			}
		}

		private void WriteEquips(Bitstream.Buffer stream)
		{
			foreach (var v in Equipped)
			{
				Bitstream.PutBits(stream, 1, 1);
				Bitstream.PutCompressedUint(stream, v.Id);
				Bitstream.PutCompressedUint(stream, v.Item.Id);
			}
			Bitstream.PutBits(stream, 1, 0);	
		}

		public override void WriteFullState(Bitstream.Buffer stream)
		{
			// which character it is.
			Bitstream.PutStringDumb(stream, CharacterTypeId);
			Bitstream.PutFloat(stream, Position.x);
			Bitstream.PutFloat(stream, Position.y);
			Bitstream.PutFloat(stream, Position.z);
			Bitstream.PutFloat(stream, Heading);
			WriteEquips(stream);
			Bitstream.PutCompressedInt(stream, (int)TimeOffset);
		}

		public override bool WriteReliableUpdate(Bitstream.Buffer stream)
		{
			if (SendNewEquip)
			{
				Bitstream.PutBits(stream, 1, 1);
				WriteEquips(stream);
				Bitstream.PutBits(stream, 1, 0);
				SendNewEquip = false;
				return true;
			}
			return false;
		}

		public override bool WriteUnreliableUpdate(Bitstream.Buffer stream)
		{
			if (GotNew)
			{
				Bitstream.PutBits(stream, 1, 0); // no equip
				Bitstream.PutBits(stream, 1, 1); // char data
				Bitstream.PutFloat(stream, Position.x);
				Bitstream.PutFloat(stream, Position.y);
				Bitstream.PutFloat(stream, Position.z);
				Bitstream.PutFloat(stream, Heading);
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
