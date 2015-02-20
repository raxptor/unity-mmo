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
            x = _x; y = _y; z = _z;
        }
    }

	public struct ServerCharacterData
	{
		public uint Id;
		public string Prefab;
		public string ScenePath;
		public Vector3 StartPosition;
		public bool HumanControllable;
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

        // controller
        public Controller Controller;

        public ServerCharacter(ServerCharacterData data)
        {
            Data = data;
			ResetFromData(data);
		}

		public void ResetFromData(ServerCharacterData data)
		{
			Position = Data.StartPosition;
			Velocity = new Vector3(0, 0, 0);
			Spawned = false;
		}

		public void Update(float dt)
		{
			if (!Spawned)
			{
				return;
			}
		}

		public void WriteFullState(Bitstream.Buffer stream)
		{

		}

		public bool WriteReliableUpdate(Bitstream.Buffer stream)
		{
			return false;
		}

		public bool WriteUnreliableUpdate(Bitstream.Buffer stream)
		{
			return false;
		}
    }
}
