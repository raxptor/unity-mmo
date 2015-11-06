using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerHumanController : Controller
	{
		private const float SCALING_MOVE     = 1.0f / 1024.0f;
		private const float SCALING_VELOCITY = 1.0f / 1024.0f;

		private List<Bitstream.Buffer> _blocks = new List<Bitstream.Buffer>();

		public void ControlMe(uint iteration, ServerCharacter character)
		{
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
					case EventBlock.Type.FIRE:
						{
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
