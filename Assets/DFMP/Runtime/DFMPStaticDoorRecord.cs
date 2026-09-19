using System;
using DaggerfallWorkshop;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// JsonUtility-friendly mirror of native <see cref="StaticDoor"/> for character persistence and reconnect reopen.
    /// </summary>
    [Serializable]
    public class DFMPStaticDoorRecord
    {
        public int BuildingKey;
        public float OwnerPosX;
        public float OwnerPosY;
        public float OwnerPosZ;
        public float OwnerRotX;
        public float OwnerRotY;
        public float OwnerRotZ;
        public float OwnerRotW = 1f;
        public float[] BuildingMatrix = CreateIdentityMatrix();
        public int DoorType;
        public int BlockIndex;
        public int RecordIndex;
        public int DoorIndex;
        public float CentreX;
        public float CentreY;
        public float CentreZ;
        public float SizeX;
        public float SizeY;
        public float SizeZ;
        public float NormalX;
        public float NormalY;
        public float NormalZ;

        public void Normalize()
        {
            if (BuildingMatrix == null || BuildingMatrix.Length != 16)
                BuildingMatrix = CreateIdentityMatrix();
            if (Mathf.Approximately(OwnerRotX, 0f) &&
                Mathf.Approximately(OwnerRotY, 0f) &&
                Mathf.Approximately(OwnerRotZ, 0f) &&
                Mathf.Approximately(OwnerRotW, 0f))
                OwnerRotW = 1f;
        }

        public bool IsValidForBuildingReopen()
        {
            Normalize();
            return BuildingKey > 0;
        }

        public StaticDoor ToStaticDoor()
        {
            Normalize();
            var door = new StaticDoor
            {
                buildingKey = BuildingKey,
                ownerPosition = new Vector3(OwnerPosX, OwnerPosY, OwnerPosZ),
                ownerRotation = new Quaternion(OwnerRotX, OwnerRotY, OwnerRotZ, OwnerRotW),
                buildingMatrix = MatrixFromArray(BuildingMatrix),
                doorType = (DoorTypes)DoorType,
                blockIndex = BlockIndex,
                recordIndex = RecordIndex,
                doorIndex = DoorIndex,
                centre = new Vector3(CentreX, CentreY, CentreZ),
                size = new Vector3(SizeX, SizeY, SizeZ),
                normal = new Vector3(NormalX, NormalY, NormalZ)
            };
            return door;
        }

        public static DFMPStaticDoorRecord FromStaticDoor(StaticDoor door)
        {
            var record = new DFMPStaticDoorRecord
            {
                BuildingKey = door.buildingKey,
                OwnerPosX = door.ownerPosition.x,
                OwnerPosY = door.ownerPosition.y,
                OwnerPosZ = door.ownerPosition.z,
                OwnerRotX = door.ownerRotation.x,
                OwnerRotY = door.ownerRotation.y,
                OwnerRotZ = door.ownerRotation.z,
                OwnerRotW = door.ownerRotation.w,
                BuildingMatrix = MatrixToArray(door.buildingMatrix),
                DoorType = (int)door.doorType,
                BlockIndex = door.blockIndex,
                RecordIndex = door.recordIndex,
                DoorIndex = door.doorIndex,
                CentreX = door.centre.x,
                CentreY = door.centre.y,
                CentreZ = door.centre.z,
                SizeX = door.size.x,
                SizeY = door.size.y,
                SizeZ = door.size.z,
                NormalX = door.normal.x,
                NormalY = door.normal.y,
                NormalZ = door.normal.z
            };
            record.Normalize();
            return record;
        }

        public static DFMPStaticDoorRecord[] FromStaticDoors(StaticDoor[] doors)
        {
            if (doors == null || doors.Length == 0)
                return new DFMPStaticDoorRecord[0];

            var records = new DFMPStaticDoorRecord[doors.Length];
            for (int index = 0; index < doors.Length; index++)
                records[index] = FromStaticDoor(doors[index]);
            return records;
        }

        public static StaticDoor[] ToStaticDoors(DFMPStaticDoorRecord[] records)
        {
            if (records == null || records.Length == 0)
                return new StaticDoor[0];

            var doors = new StaticDoor[records.Length];
            for (int index = 0; index < records.Length; index++)
                doors[index] = records[index] != null ? records[index].ToStaticDoor() : default(StaticDoor);
            return doors;
        }

        static float[] CreateIdentityMatrix()
        {
            return new float[]
            {
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f
            };
        }

        static float[] MatrixToArray(Matrix4x4 matrix)
        {
            var values = new float[16];
            for (int index = 0; index < 16; index++)
                values[index] = matrix[index];
            return values;
        }

        static Matrix4x4 MatrixFromArray(float[] values)
        {
            var matrix = Matrix4x4.identity;
            if (values == null || values.Length != 16)
                return matrix;

            for (int index = 0; index < 16; index++)
                matrix[index] = values[index];
            return matrix;
        }
    }
}
