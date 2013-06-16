﻿using MBC.Shared;

namespace MBC.Core.Events
{
    public class ControllerTimeoutEvent : ControllerEvent
    {
        private int time;
        private string methodName;

        public ControllerTimeoutEvent(ControllerTimeoutException exception)
            : base(exception.Register)
        {
            Init(exception.Register, exception.TimeTaken, exception.MethodName);
        }

        private void Init(ControllerRegister register, int time, string method)
        {
            this.time = time;
            this.methodName = method;

            message = register + " went over the time limit of " + register.Match.TimeLimit +
                "ms by " + (time - register.Match.TimeLimit) + "ms on the invoke of " + method + ".";
        }

        public int TimeTaken
        {
            get
            {
                return time;
            }
        }

        public string Method
        {
            get
            {
                return methodName;
            }
        }
    }
}