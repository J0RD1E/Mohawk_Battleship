﻿using MBC.Core.Util;
using MBC.Shared;
using System;
using System.Diagnostics;
using System.Threading;

namespace MBC.Core
{
    /// <summary>
    /// Loads a <see cref="Controller"/> from <see cref="ControllerInformation"/>. Wraps the <see cref="Controller"/>
    /// to invoke its methods in a different thread and prevent hang-ups due to
    /// <see cref="Controller"/>s taking too long to complete method calls.
    /// </summary>
    [Configuration("mbc_controller_thread_timeout", 500)]
    public class ControllerUser
    {
        private Controller controller;

        private ControllerInformation controllerInfo;

        private int maxTimeout;
        private Stopwatch timeElapsed;

        /// <summary>
        /// Initializes internal variables and creates the <see cref="Controller"/> that is specified by
        /// the given <see cref="ControllerInformation"/>.
        /// </summary>
        /// <param name="targetControllerInfo">The <see cref="ControllerInformation"/> to create a
        /// <see cref="Controller"/> from.</param>
        public ControllerUser(ControllerInformation targetControllerInfo)
        {
            this.controllerInfo = targetControllerInfo;
            this.timeElapsed = new Stopwatch();
            this.maxTimeout = Configuration.Global.GetValue<int>("mbc_controller_thread_timeout");

            controller = (Controller)Activator.CreateInstance(targetControllerInfo.Controller);
            controller.ControllerMessageEvent += ReceiveMessage;
        }

        /// <summary>
        /// Occurs whenever the <see cref="Controller"/> outputs a message string.
        /// </summary>
        public event StringOutputHandler ControllerMessageEvent;

        /// <summary>
        /// Gets the <see cref="ControllerRegister"/> given by a <see cref="Match"/>.
        /// </summary>
        public Register Register
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the time (in milliseconds) that the <see cref="Controller"/> took to finish the last method.
        /// </summary>
        public int TimeElapsed
        {
            get
            {
                return (int)timeElapsed.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// Uses the <see cref="Controller"/>'s <see cref="Controller.MakeShot()"/> method to get a <see cref="Shot"/>.
        /// </summary>
        /// <returns>A <see cref="Shot"/> generated by the <see cref="Controller"/>. The <see cref="Shot"/>
        /// returned may be null if the <see cref="Controller"/> returns null.</returns>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public Shot MakeShot()
        {
            Shot result = null;
            var thread = new Thread(() => result = controller.MakeShot());

            HandleThread(thread, "GetShot");

            return result;
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that a match is over.
        /// </summary>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void MatchOver()
        {
            var thread = new Thread(() =>
            controller.MatchOver());

            HandleThread(thread, "MatchOver");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that a match has begin, and updates the <see cref="ControllerRegister"/>
        /// to the one given by the <see cref="Match"/>. Provides a complete copy of the <see cref="ControllerRegister"/>
        /// to the <see cref="Controller"/>.
        /// </summary>
        /// <param name="registerInstance">The <see cref="ControllerRegister"/> for the <see cref="Controller"/>.</param>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void NewMatch(Register registerInstance)
        {
            Register = registerInstance;
            Register.Score = 0;
            controller.Register = new ControllerRegister(Register);

            var thread = new Thread(() =>
            controller.NewMatch());

            HandleThread(thread, "NewMatch");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that a new round has begun. Resets the <see cref="Controller"/>'s
        /// <see cref="ControllerRegister"/> copy.
        /// </summary>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void NewRound()
        {
            var thread = new Thread(() => controller.NewRound());

            HandleThread(thread, "NewRound");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> about a shot made against it by another <see cref="Controller"/>.
        /// </summary>
        /// <param name="opShot">The <see cref="Shot"/> made by an opposing <see cref="Controller"/>.</param>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void NotifyOpponentShot(Shot opShot)
        {
            var thread = new Thread(() => controller.OpponentShot(opShot));

            HandleThread(thread, "OpponentShot");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that a previous <see cref="Shot"/> made by it hit another opponent, with
        /// a value indicating if it sunk a <see cref="Ship"/>.
        /// </summary>
        /// <param name="shotMade">The <see cref="Shot"/> made earlier by the <see cref="Controller"/>.</param>
        /// <param name="sink">A value indicating if the <see cref="Shot"/> sunk a <see cref="Ship"/>.</param>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void NotifyShotHit(Shot shotMade, bool sink)
        {
            var thread = new Thread(() => controller.ShotHit(shotMade, sink));

            HandleThread(thread, "ShotHit");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> about a <see cref="Shot"/> previously made that did not
        /// hit an opposing <see cref="Ship"/>.
        /// </summary>
        /// <param name="shotMade">The <see cref="Shot"/> made that did not hit a <see cref="Ship"/>.</param>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void NotifyShotMiss(Shot shotMade)
        {
            var thread = new Thread(() => controller.ShotMiss(shotMade));

            HandleThread(thread, "ShotMiss");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that it must begin to place the <see cref="Ship"/>s in
        /// its <see cref="ControllerRegister"/>. Copies the <see cref="ShipList"/> in its <see cref="ControllerRegister"/>.
        /// </summary>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public ShipList PlaceShips()
        {
            ShipList result = null;
            var thread = new Thread(() => result = controller.PlaceShips(Register.Match.StartingShips));

            HandleThread(thread, "PlaceShips");
            return result;
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that it lost a <see cref="Round"/>.
        /// </summary>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void RoundLost()
        {
            var thread = new Thread(() =>
            controller.RoundLost());

            HandleThread(thread, "RoundLost");
        }

        /// <summary>
        /// Notifies the <see cref="Controller"/> that it won a <see cref="Round"/>.
        /// </summary>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        public void RoundWon()
        {
            Register.Score++;
            controller.Register.Score++;

            var thread = new Thread(() =>
            controller.RoundWon());

            HandleThread(thread, "RoundWon");
        }

        /// <summary>
        /// Gets the generated name of the <see cref="ControllerInformation"/> constructed with.
        /// </summary>
        /// <returns>The string of a <see cref="ControllerInformation"/>.</returns>
        public override string ToString()
        {
            return controllerInfo.ToString();
        }

        /// <summary>
        /// Runs a <see cref="Thread"/> and waits for it to finish for a time as defined in the <see cref="Configuration"/>.
        /// Throws a <see cref="ControllerTimeoutException"/> if the time limit has been exceeded.
        /// </summary>
        /// <param name="thread">The thread to start.</param>
        /// <param name="method">The name of the method of the <see cref="Controller"/> being ran.
        /// Used as information for a <see cref="ControllerTimeoutException"/>.</param>
        /// <exception cref="ControllerTimeoutException">Thrown if the controller exceeded the time limit specified
        /// in the <see cref="MatchInfo"/> located in the <see cref="ControllerRegister"/>.</exception>
        private void HandleThread(Thread thread, string method)
        {
            //Start the thread.
            timeElapsed.Restart();
            thread.Start();
            if (!thread.Join(maxTimeout))
            {
                //Thread timed out.
                thread.Abort();
            }
            timeElapsed.Stop();
            if (TimeElapsed > Register.Match.TimeLimit)
            {
                throw new ControllerTimeoutException(Register, method, TimeElapsed);
            }
        }

        /// <summary>
        /// This method is subscribed to the <see cref="Controller"/>'s <see cref="Controller.ControllerMessageEvent"/>.
        /// </summary>
        /// <param name="message">A string containing the message.</param>
        private void ReceiveMessage(string message)
        {
            if (ControllerMessageEvent != null)
            {
                ControllerMessageEvent(message);
            }
        }
    }
}