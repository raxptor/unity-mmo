using System;

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
    }

    public class ServerCharacter
    {
        // locations
        public ServerCharacterData Data;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Heading;

        // controller
        public Controller Controller;

        public ServerCharacter(ServerCharacterData data)
        {
            Data = data;
            Position = data.StartPosition;
            Velocity = new Vector3(0, 0, 0);
            Heading = 0;
        }
    }
}
