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
		public uint HP = 100;
	}

	public class ServerCharacter : Entity
	{
		// locations
		public ServerCharacterData Data;
		public ServerPlayer Player;
		public WorldServer World;

		public bool Spawned = false;
		public bool Dead = false;

		public Vector3 Position;
		public Vector3 Velocity;
		public float Heading = 0;

		public uint HP, MaxHP;

		public List<ServerPlayer.ItemInstance> Equipped = new List<ServerPlayer.ItemInstance>(); 
		public bool SendNewEquip = false;

		public string CharacterTypeId;
		public float TimeOffset = 0;

		public bool GotNew = false;

		// outgoing events
		public List<Bitstream.Buffer> Events = new List<Bitstream.Buffer>();

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
			Dead = false;
			HP = MaxHP = data.HP;
			Equipped.Clear();
		}

		// For testing only.
		public void MirrorIt(ServerCharacter from, Vector3 ofs)
		{
			Equipped = from.Equipped;
			Data = from.Data;
			Position = from.Position + ofs;
			Velocity = from.Velocity;
			Heading = from.Heading;
			Spawned = from.Spawned;
			CharacterTypeId = from.CharacterTypeId;
			TimeOffset = from.TimeOffset;
			GotNew = from.GotNew;
		}

		public bool Alive()
		{
			return !Dead && Spawned;
		}

		public void TakeDamage(int amt)
		{
			if (amt > 0)
			{
				if (amt >= HP)
				{
					HP = 0;
					OnDeath();
				}
				else
				{
					HP = (uint)(HP - amt);
				}
				GotNew = true;
			}
		}

		private void OnDeath()
		{
			AddAnimEvent("Character " + m_Id + " died!");
			Dead = true;
			GotNew = true;
		}

		public void AddAnimEvent(string anim)
		{
			Bitstream.Buffer b = Bitstream.Buffer.Make(new byte[128]);
			Bitstream.PutCompressedUint(b, 0); // anim
			Bitstream.PutStringDumb(b, anim);
			b.Flip();
			Events.Add(b);
		}

		public uint AmmoInWeapon(ServerPlayer.ItemInstance item)
		{
			if (item.Item.Weapon == null)
				return 0;
			if (item.Children == null)
				return 0;

			uint sum = 0;
			foreach (var se in item.Children)
			{
				if (se.Item.Ammo != null && se.Item.Ammo.AmmoType == item.Item.Weapon.AmmoType)
					sum += se.Count;
			}

			return sum;
		}

		public bool UseAmmoInWeapon(ServerPlayer.ItemInstance item)
		{
			if (item.Item.Weapon == null)
				return false;
			if (item.Children == null)
				return false;

			uint sum = 0;
			foreach (var se in item.Children)
			{
				if (se.Item.Ammo != null && se.Item.Ammo.AmmoType == item.Item.Weapon.AmmoType)
				{
					se.Count--;
					if (se.Count == 0)
					{
						item.Children.Remove(se);
						return true;
					}
					return true;
				}
			}
			return false;
		}

		public void Reload(uint itemId)
		{
			int toFill = 0;
			outki.AmmoType type = outki.AmmoType.AMMO_TYPE_0;
			ServerPlayer.ItemInstance target = null;

			foreach (var entry in Player.Inventory)
			{
				if (entry.Id == itemId)
				{
					if (entry.Item.Weapon == null)
						return;

					target = entry;
					type = entry.Item.Weapon.AmmoType;
					toFill = (int)entry.Item.Weapon.Capacity - (int)AmmoInWeapon(entry);
					Console.WriteLine("I can fill " + toFill + " ammo: " + type);
					break;
				}
			}

			if (target == null || toFill == 0)
				return;

			outki.Item _fillWith = null;
			int got = 0;

			for (int i = 0; i < Player.Inventory.Count && toFill > 0; i++)
			{
				ServerPlayer.ItemInstance ii = Player.Inventory[i];
				if (ii.Item.Ammo != null && ii.Item.Ammo.AmmoType == type)
				{
					_fillWith = ii.Item;
					if (ii.Count > toFill)
					{
						got = toFill;
						ii.Count -= (uint)toFill;
						Player.InventoryChanged = true;
						break;
					}
					else
					{
						got = (int)ii.Count;
						toFill -= (int)ii.Count;
						Player.Inventory.RemoveAt(i);
						i--;
					}
				}
			}

			if (_fillWith != null)
			{
				ServerPlayer.ItemInstance itm = World.MakeItem(_fillWith, (uint)got, 0);
				if (target.Children == null)
					target.Children = new List<ServerPlayer.ItemInstance>();
				target.Children.Add(itm);
				Player.InventoryChanged = true;
			}
		}

		public override void Update(uint iteration, float dt)
		{

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
			Bitstream.PutCompressedUint(stream, HP);
			Bitstream.PutBits(stream, 1, (uint)(Dead ? 1 : 0));
			WriteEquips(stream);
			Bitstream.PutCompressedInt(stream, (int)TimeOffset);
		}

		public override bool WriteReliableUpdate(Bitstream.Buffer stream)
		{
			if (!SendNewEquip && Events.Count == 0)
				return false;
			
			if (SendNewEquip)
			{
				SendNewEquip = false;
				Bitstream.PutBits(stream, 1, 1);
				WriteEquips(stream);
			}
			else
			{
				Bitstream.PutBits(stream, 1, 0);
			}

			if (Events.Count > 0)
			{
				Bitstream.PutBits(stream, 1, 1);
				Bitstream.PutCompressedUint(stream, (uint)Events.Count);
				for (int i = 0; i < Events.Count; i++)
				{
					Bitstream.Insert(stream, Events[i]);
				}
				Events.Clear();
			}
			else
			{
				Bitstream.PutBits(stream, 1, 0);
			}
					
			// no character
			Bitstream.PutBits(stream, 1, 0);
			return true;
		}

		public override bool WriteUnreliableUpdate(Bitstream.Buffer stream)
		{
			if (GotNew)
			{
				Bitstream.PutBits(stream, 1, 0); // no equip
				Bitstream.PutBits(stream, 1, 0); // no events
				Bitstream.PutBits(stream, 1, 1); // char data
				Bitstream.PutFloat(stream, Position.x);
				Bitstream.PutFloat(stream, Position.y);
				Bitstream.PutFloat(stream, Position.z);
				Bitstream.PutFloat(stream, Heading);
				Bitstream.PutFloat(stream, Velocity.x);
				Bitstream.PutFloat(stream, Velocity.y);
				Bitstream.PutFloat(stream, Velocity.z);
				Bitstream.PutCompressedInt(stream, (int)TimeOffset);
				Bitstream.PutCompressedUint(stream, HP);
				Bitstream.PutBits(stream, 1, (uint)(Dead ? 1 : 0));
				GotNew = false;
				return true;
			}
			return false;
		}
	}
}
