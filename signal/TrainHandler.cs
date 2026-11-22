#pragma warning disable IDE0290

using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public class TrainHandler
    {
        private readonly IMySensorBlock Sensor1;
        private readonly IMySensorBlock Sensor2;
        private readonly IMyIntergridCommunicationSystem IGC;
        
        public TrainHandler(IMyIntergridCommunicationSystem igc, IMySensorBlock sensor1, IMySensorBlock sensor2)
        {
            Sensor1 = sensor1;
            Sensor2 = sensor2;
            IGC = igc;
        }

        private ErrorCode SendMessage(string message)
        {
            IGC.SendBroadcastMessage(Constants.ICS_SIGNAL2TRAIN_TAG, message, TransmissionDistance.TransmissionDistanceMax);

            return ErrorCode.NONE;
        }

        public ErrorCode SendData(int speed, Status status, int nextSignalSpeed, Status nextSignalStatus)
        {
            string message = $"{speed},{status},{nextSignalSpeed},{nextSignalStatus}";
            return SendMessage(message);
        }

        public ErrorCode GetTrainStatus(out TrainStatus status)
        {;
            if (!(Sensor1.IsActive && Sensor2.IsActive))
            {
                status =  TrainStatus.UNDETECTED;
                return ErrorCode.NONE;
            }

            MyDetectedEntityInfo entity1 = Sensor1.LastDetectedEntity;
            MyDetectedEntityInfo entity2 = Sensor2.LastDetectedEntity;

            if (entity1.EntityId != entity2.EntityId)
            {
                status = TrainStatus.ERROR;
                return ErrorCode.TOO_MUCH_ENTITIES_DETECTED;
            }

            // from here, entity1.EntityId == entity2.EntityId, so we can use either one to get the speed
            if (entity1.Velocity.LengthSquared() < 0.1)
            {
                status = TrainStatus.STOPPED;
                return ErrorCode.NONE;
            }
            
            if (entity1.TimeStamp < entity2.TimeStamp)
            {
                status = TrainStatus.FORWARD;
            }
            else if (entity1.TimeStamp > entity2.TimeStamp)
            {
                status = TrainStatus.BACKWARD;
            }
            else
            {
                status = TrainStatus.ERROR;
                return ErrorCode.TRAIN_TOO_FAST;
            }

            return ErrorCode.NONE;
        } 

    }
}
