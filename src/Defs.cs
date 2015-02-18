using netki;

namespace UnityMMO
{
	public static class UpdateMangling
	{
		public const int TYPE_BITS = 4;
		public const uint UPDATE_FILTER = 1;

		public static void BlockHeader(Bitstream.Buffer buf, uint type)
		{
			Bitstream.PutBits(buf, TYPE_BITS, type);
		}
	}
}
