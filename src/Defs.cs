using netki;

namespace UnityMMO
{
	public static class DatagramCoding
	{
		public const int TYPE_BITS = 3;
		public const int TYPE_CONTROL = 1;
		public const int TYPE_UPDATE  = 2;
		public const int TYPE_PACKET  = 4;
	}

	public static class UpdateMangling
	{
		public const int TYPE_BITS = 4;
		public const uint UPDATE_FILTER      = 1;
		public const uint UPDATE_CHARACTERS  = 2;

		public static void BlockHeader(Bitstream.Buffer buf, uint type)
		{
			Bitstream.PutBits(buf, TYPE_BITS, type);
		}
	}

	public static class ControlBlock
	{
		public const int TYPE_BITS = 10;
		public const uint EVENT_SPAWN         = 0;
		public const uint EVENT_FIRE          = 1;
		public const uint EVENT_MOVE          = 2;
		public const uint EVENT_CHANGE_WEAPON = 3;
		public const uint EVENT_RELOAD        = 4;

		public static void BlockHeader(Bitstream.Buffer buf, uint type)
		{
			Bitstream.PutBits(buf, TYPE_BITS, type);
		}
	}
}
