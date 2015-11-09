using System.Collections.Generic;
using netki;
using System;

namespace UnityMMO
{
	public class ServerHumanController : Controller
	{
		private const float SCALING_MOVE     = 1.0f / 1024.0f;
		private const float SCALING_VELOCITY = 1.0f / 1024.0f;

		private List<Bitstream.Buffer> _blocks = new List<Bitstream.Buffer>();

		public class HumanData
		{
			public uint UnspawnTimer;
			public uint LastIteration;
		}

		public void OnHit(uint iteration, ServerCharacter character, Entity inflictor,  string hitbox, int amount)
		{

		}

		public void ControlMe(uint iteration, ServerCharacter character)
		{
			if (character.ControllerData == null)
			{
				HumanData hd = new HumanData();
				hd.UnspawnTimer = 2000;
				hd.LastIteration = iteration;
				character.ControllerData = hd;
			}

			HumanData d = (HumanData)character.ControllerData;
			uint delta = iteration - d.LastIteration;
			d.LastIteration = iteration;

			if (character.Spawned && character.Dead)
			{
				if (delta >= d.UnspawnTimer)
				{
					System.Console.WriteLine("Unspawning human character.");
					character.Spawned = false;
					character.ControllerData = null;
					character.GotNew = true;
					return;
				}
				d.UnspawnTimer -= delta;
			}

			foreach (Bitstream.Buffer buf in _blocks)
			{
				ExecEventBlock(iteration, buf, character);
			}
		}

		private void ExecEventBlock(uint iteration, Bitstream.Buffer buf, ServerCharacter character)
		{
			bool Alive = character.Spawned && !character.Dead;

			while (true)
			{
				uint evt = Bitstream.ReadBits(buf, EventBlock.TYPE_BITS);
				if (buf.error != 0)
					break;

				switch ((EventBlock.Type)evt)
				{
					case EventBlock.Type.MOVE:
						{
							Vector3 pos, vel;
							uint local_time = Bitstream.ReadCompressedUint(buf);
							NetUtil.ReadScaledVec3(buf, SCALING_MOVE, out pos);
							NetUtil.ReadScaledVec3(buf, SCALING_VELOCITY, out vel);
							byte heading = (byte) Bitstream.ReadBits(buf, 8);

							if (Alive)
							{
								if (buf.error == 0)
								{
									character.TimeOffset = (int)local_time - (int)iteration;
									character.Position = pos;
									character.Velocity = vel;
									character.Heading = heading * 3.1415f / 128.0f;
									character.GotNew = true;
								}
							}
							break;
						}
					case EventBlock.Type.RELOAD:
						{
							foreach (var x in character.Equipped)
							{
								character.Reload(x.Id);	
							}
						}
						break;

					case EventBlock.Type.FIRE:
						{
							uint itemInstanceID = Bitstream.ReadCompressedUint(buf);
							string hitTarget = Bitstream.ReadStringDumb(buf);
							string animName = Bitstream.ReadStringDumb(buf);
							uint characterId = Bitstream.ReadCompressedUint(buf);
							Vector3 localModelPos;
							NetUtil.ReadScaledVec3(buf, 0.001f, out localModelPos);
							Console.WriteLine("FIRE! with hitbox [" + hitTarget + "] and anim[" + animName + "]");
							if (Alive)
							{
								ServerPlayer.ItemInstance ii = character.Player.GetInventoryItem(itemInstanceID);
								if (ii == null)
									break;
								if (ii.Item.Weapon == null)
									break;

								if (ii.Item.Weapon.Type != outki.WeaponType.WEAPONTYPE_MELEE)
								{
									if (!character.UseAmmoInWeapon(ii))
									{
										Console.WriteLine("Canont use ammo in weapon");
										break;
									}
								}
								else
								{
									// TODO: Track stamina.
								}

								character.Player.InventoryChanged = true;
								
								if (hitTarget != "")
								{
									System.Console.WriteLine("Hit " + characterId + "!");
									if (characterId < character.World._activeCharacters.Count)
									{
										int amt = 10;
										ServerCharacter tgt = character.World._activeCharacters[(int)characterId];
										tgt.TakeDamage(amt);

										Bitstream.Buffer hitBuf = Bitstream.Buffer.Make(new byte[256]);
										Bitstream.PutCompressedUint(hitBuf, 1); // type: hit
										Bitstream.PutCompressedUint(hitBuf, character.m_Id);
										NetUtil.PutScaledVec3(hitBuf, 0.001f, localModelPos);
										Bitstream.PutCompressedInt(hitBuf, amt);
										hitBuf.Flip();
										tgt.Events.Add(hitBuf);

										if (tgt.Controller != null)
											tgt.Controller.OnHit(iteration, tgt, character, hitTarget, amt);
									}
								}

								Bitstream.Buffer fireBuf = Bitstream.Buffer.Make(new byte[256]);
								Bitstream.PutCompressedUint(fireBuf, 2); // type: fire
								Bitstream.PutStringDumb(fireBuf, animName);
								fireBuf.Flip();
								character.Events.Add(fireBuf);
							}
							break;
						}
					case EventBlock.Type.INTERACT:
						{
							uint entityId = Bitstream.ReadCompressedUint(buf);
							if (Alive)
							{
								foreach (Entity e in character.World._activeEntities)
								{
									if (e.m_Id == entityId)
									{
										e.OnInteract(iteration, character);
									}
								}
							}
							break;
						}
					case EventBlock.Type.SPAWN:
						{
							string CharacterId = Bitstream.ReadStringDumb(buf);
							uint local_time = Bitstream.ReadCompressedUint(buf);

							if (character.Spawned)
							{
								// Debug.Log("Spawn: Player already spawned");
								break;
							}

							Debug.Log("Spawning character [" + CharacterId + "] on player");

							Vector3 spawnPos;
							if (!character.World.GetPointForHumanSpawn(out spawnPos))
							{
								Debug.Log("Spawn: Could not get spawn point");
								break;
							}

							character.World.ResetPlayerToDefaults(character.Player);
							character.World.ResetCharacter(character.Player, character);

							character.Dead = false;
							character.Position = spawnPos;
							character.CharacterTypeId = CharacterId;
							character.Spawned = true;
							character.TimeOffset = (int)local_time - (int)iteration;
							character.GotNew = true;
							break;
						}
				}
			}
		}

		public void OnControlBlock(Bitstream.Buffer buf)
		{
			_blocks.Add(buf);
		}
	}
}
