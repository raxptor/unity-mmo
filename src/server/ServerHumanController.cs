using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerHumanController : Controller
	{
		private const float SCALING_MOVE     = 1.0f / 1024.0f;
		private const float SCALING_VELOCITY = 1.0f / 1024.0f;

		private List<Bitstream.Buffer> _blocks = new List<Bitstream.Buffer>();

		public void ControlMe(ServerCharacter character)
		{
			foreach (Bitstream.Buffer buf in _blocks)
			{
				ExecEventBlock(buf, character);
			}
		}

		private void ExecEventBlock(Bitstream.Buffer buf, ServerCharacter character)
		{
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
							NetUtil.ReadScaledVec3(buf, SCALING_MOVE, out pos);
							NetUtil.ReadScaledVec3(buf, SCALING_VELOCITY, out vel);
							byte heading = (byte) Bitstream.ReadBits(buf, 8);
							if (buf.error != 0)
							{
								character.Position = pos;
								character.Velocity = vel;
								character.Heading = heading * 3.1415f / 128.0f;
							}
							break;
						}
					case EventBlock.Type.FIRE:
						{
							break;
						}
					case EventBlock.Type.SPAWN:
						{
							uint CharacterHash = Bitstream.ReadCompressedUint(buf);
							if (character.Spawned)
							{
								Debug.Log("Spawn: Player already spawned");
								break;
							}
							character.CharacterHash = CharacterHash;
							character.Spawned = true;
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
