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
				ExecControlBlock(buf, character);
			}
		}

		private void ExecControlBlock(Bitstream.Buffer buf, ServerCharacter character)
		{
			while (true)
			{
				uint evt = Bitstream.ReadBits(buf, ControlBlock.TYPE_BITS);
				if (buf.error != 0)
					break;

				switch (evt)
				{
					case ControlBlock.EVENT_MOVE:
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
						}
						break;
					case ControlBlock.EVENT_FIRE:
						{
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
