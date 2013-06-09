﻿namespace MBC.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Threading;

    /// <summary>A Competition object contains game logic for a game of battleship.
    /// 
    /// A competition has 3 events that are called internally during game play. Add methods to be invoked to these
    /// events as needed.
    /// 
    /// <list type="bullet">
    ///      <item>RoundTurnEndEvent - Called when a controller has ended their turn.</item>
    ///      <item>RoundEndEvent - Called when a round has ended.</item>
    ///      <item>MatchEndEvent - Called when a matchup has ended (from any methods invoking RunRounds)</item>
    /// </list>
    ///      
    /// The competition can be run either in the same thread as the calling method (RunCompetition()), or
    /// in a different thread (RunCompetitionThread()), which can be stopped at any time (StopCompetitionThread()).
    /// By using one of these methods, the Competition object will use the Configuration object that it has
    /// been constructed with to determine the number of rounds to play. Using RunRounds(int, bool) will run
    /// a custom number of rounds in the same thread, but use of this method is not recommended.
    /// 
    /// The competition can be run in a turn-by-turn manner by utilizing three methods:
    /// 
    /// <list type="bullet">
    ///      <item>NewRound() - Starts a new round in the competition. Finishes any ongoing round.</item>
    ///      <item>RoundTurn() - Run a turn in the round.</item>
    ///      <item>RoundRun() - Run through remaining turns in the round.</item>
    /// </list>
    ///      
    /// When constructing a new Competition, keep in mind that the order of the array of IBattleshipControllers
    /// does not change, even in the Field object that is generated.
    /// </summary>
    public class Competition
    {
        
        /// <summary>Sets default configuration values for keys that relate to this class.
        /// Should be called before using the global Configuration.Default object.</summary>
        /// <seealso cref="Configuration"/>
        public static void SetConfigDefaults()
        {
            Configuration.Default.SetValue<int>("mbc_random_seed", Environment.TickCount);
            Configuration.Default.SetValue<int>("mbc_field_width", 10);
            Configuration.Default.SetValue<int>("mbc_field_height", 10);
            Configuration.Default.SetValue<string>("mbc_field_ship_sizes", "2,3,3,4,5");
            Configuration.Default.SetValue<int>("mbc_timeout", 500);
            Configuration.Default.SetValue<int>("mbc_rounds", 110);
            Configuration.Default.SetValue<bool>("mbc_playout", false);
        }
        private Controller[] controllers;     //Guaranteed to always be a 2-element array.
        private Configuration configuration;            //Configuration used for this competition
        private Field field;              //Contains field information
        private List<RoundLog> roundList;
        private RoundLog roundLogger;               //The current RoundLog for the current round

        private Thread competitionThread;                  //The thread used for tasking this competition 
        private bool isRunning = false;             //Should be false whenever rounds should stop running.

        private Controller turn;            //The current opponent's turn

        /// <summary>
        /// Notification of a controller ending their turn.
        /// </summary>
        public delegate void RoundTick();

        /// <summary>
        /// Fires when a controller has ended their turn.
        /// </summary>
        public event RoundTick RoundTurnEndEvent;

        /// <summary>
        /// Notification of a round in the competition  ending.
        /// </summary>
        public delegate void RoundEnd();

        /// <summary>
        /// Fires when a round has finished.
        /// </summary>
        public event RoundEnd RoundEndEvent;

        /// <summary>
        /// Notification of the end of a matchup in the competition.
        /// </summary>
        public delegate void MatchEnd();

        /// <summary>
        /// Fires when a matchup in the competition has finished.
        /// </summary>
        public event MatchEnd MatchEndEvent;

        
        /// <summary>Constructs a new Competition object. A Configuration object is to be provided
        /// to set the behaviour of this Competition.</summary>
        /// <param name="configuration">The Configuration to utilize. See SetConfigDefaults()</param>
        /// <param name="battleshipControllers">The controllers to create a matchup with.</param>
        public Competition(IBattleshipController[] battleshipControllers, Configuration configuration)
        {
            init(battleshipControllers, configuration, configuration.GetValue<int>("mbc_random_seed"));
        }

        
        /// <summary>Constructs a new Competition object. A Configuration object is to be provided
        /// to set the behaviour of this Competition, and a random seed number must be provided to
        /// set the internal random number generator.</summary>
        /// <param name="configuration">The Configuration to utilize. See SetConfigDefaults()</param>
        /// <param name="battleshipControllers">The controllers to create a matchup with.</param>
        /// <param name="seedNumber">The random seed number to create a desired outcome.</param>
        public Competition(IBattleshipController[] battleshipControllers, Configuration configuration, int seedNumber)
        {
            init(battleshipControllers, configuration, seedNumber); 
        }

        
        /// <summary>Called only by the constructor, does initialization of this class. See the constructors
        /// Competition().</summary>
        private void init(IBattleshipController[] battleshipControllers, Configuration configuration, int seedNumber)
        {
            this.configuration = configuration;
            field = new Field(battleshipControllers);
            field.FixedRandom = new Random(seedNumber);
            field.GameSize = new Size(configuration.GetValue<int>("mbc_field_width"), configuration.GetValue<int>("mbc_field_height"));
            field.ShipSizes = configuration.GetList<int>("mbc_field_ship_sizes");
            field.TimeoutLimit = new TimeSpan(0, 0, 0, 0, configuration.GetValue<int>("mbc_timeout"));

            roundList = new List<RoundLog>();

            controllers = new Controller[2];
            controllers[0] = new Controller(battleshipControllers[0], field, 0);
            controllers[1] = new Controller(battleshipControllers[1], field, 1);

            competitionThread = new Thread(RunCompetition);
        }

        
        /// <summary>A Getter method used to get the Field associated with this Competition.</summary>
        /// <returns>The battlefield associated with this competition</returns>
        /// <seealso cref="Field"/>
        public Field BattleField { get { return field; } }
        [Obsolete]
        public Field GetBattlefield()
        {
            return field;
        }

        
        /// <summary>Gets the rounds that are in progress or completed. Useful for invoking GetRoundLogAt().</summary>
        /// <returns>The number of rounds played, complete or incomplete</returns>
        public int RoundCount { get { return roundList.Count; } }
        [Obsolete]
        public int GetRoundCount()
        {
            return roundList.Count;
        }

        /// <summary>Gets a RoundLog object associated with a specific round number.</summary>
        /// <param name="index">The round number (starting at 0).</param>
        /// <returns>A RoundLog object</returns>
        public RoundLog GetRoundLogAt(int index)
        {
            lock(roundList) {
                if (index < 0 || index > roundList.Count)
                    return null;
                return roundList.ElementAt(index);
            }
        }

        /// <summary>Creates a list of new ships to be placed</summary>
        /// <returns>A list of ships not placed on the board, defined to having a length specified by shipSizes</returns>
        private List<Ship> GenerateNewShips()
        {
            return (from s in field.ShipSizes
                    select new Ship(s)).ToList();
        }

        
        /// <summary>Used to notify both players whether they won or lost.</summary>
        /// <returns>Always returns true</returns>
        private bool GameResultPush(Controller winner, Controller loser, string reason)
        {
            winner.GameWon();
            loser.GameLost();
            roundLogger.PutAction(new RoundLog.RoundActivity(null, winner.FieldIndex, RoundLog.RoundAction.Won));
            roundLogger.PutAction(new RoundLog.RoundActivity(null, loser.FieldIndex, RoundLog.RoundAction.Lost));
            roundLogger.PutAction(new RoundLog.RoundActivity(reason, -1, RoundLog.RoundAction.RoundEnd));
            return true;
        }

        
        /// <returns>The opposing BattleshipOpponent relative to 'ibc'</returns>
        private Controller Opponent(Controller controller)
        {
            foreach (Controller c in controllers)
                if (controller != c)
                    return c;
            return null;
        }

        
        /// <summary>Notifys each player that a new game is commencing</summary>
        /// <returns>True if the game is over, false if the game is still on-going.</returns>
        private bool NewGame()
        {
            foreach (Controller controller in controllers)
                if (controller.NewGame())
                    return GameResultPush(Opponent(controller), controller, RoundLog.RoundActivity.Reason_Timeout);
            return false;
        }

        
        /// <summary>Ship placement mode for the players. Repeats until ships have been placed validly</summary>
        /// <returns>True if the game is over. False if the game is on-going</returns>
        private bool ShipPlacement()
        {
            foreach (Controller controller in controllers)
                do
                {
                    if (controller.PlaceShips(GenerateNewShips()))
                        return GameResultPush(Opponent(controller), controller, RoundLog.RoundActivity.Reason_Timeout);
                    RoundLog.RoundActivity action = new RoundLog.RoundActivity(controller.ShipsReady() ? "Ready" : "Invalid", controller.FieldIndex, RoundLog.RoundAction.ShipsPlaced);
                    action.fieldState = new Field(field);
                    roundLogger.PutAction(action);
                } while (!controller.ShipsReady());
            return false;
        }

        
        /// <summary>The main game logic of battleship.</summary>
        /// <returns>True if the game is over (the method never returns false)</returns>
        private bool GamePlay()
        {
            Point shot = turn.ShootAt(Opponent(turn));

            if (shot.X == Controller.MagicNumberLose && shot.Y == Controller.MagicNumberLose)
                return GameResultPush(Opponent(turn), turn, RoundLog.RoundActivity.Reason_Timeout);
            if (Opponent(turn).OpponentShot(shot))
                return GameResultPush(turn, Opponent(turn), RoundLog.RoundActivity.Reason_Timeout);

            Ship shipHit = Opponent(turn).GetShipAtPoint(shot);

            if (shipHit != null)
            {
                roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIndex, RoundLog.RoundAction.ShotAndHit, turn.GetTimeTaken()));

                bool sunk = shipHit.IsSunk(turn.ControllerInformation.ShotsMade);

                turn.ShotHit(shot, sunk);
                if (sunk)
                    roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIndex, RoundLog.RoundAction.ShipDestroyed, turn.GetTimeTaken()));
            }
            else
            {
                roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIndex, RoundLog.RoundAction.ShotAndMiss, turn.GetTimeTaken()));
                turn.ShotMiss(shot);
            }

            //Are there any ships left from the other opponent?
            if (!Opponent(turn).IsAlive(turn.ControllerInformation.ShotsMade))
                return GameResultPush(turn, Opponent(turn), null);
            return false;
        }

        
        /// <summary>Part of manual round control. Clears the previous round if it exists
        /// and starts a new round</summary>
        /// <returns>True if the round is ongoing, false if it has been won</returns>
        /// <seealso cref="RoundTurn()"/>
        /// <seealso cref="RunRound()"/>
        public bool NewRound()
        {
            lock (roundList)
            {
                if (roundLogger != null && roundLogger.GetAt(roundLogger.GetActivityCount() - 1).action != RoundLog.RoundAction.RoundEnd)
                    roundLogger.PutAction(new RoundLog.RoundActivity("Round ended before win", -1, RoundLog.RoundAction.RoundEnd));

                roundLogger = new RoundLog();
                roundList.Add(roundLogger);

                turn = controllers[field.FixedRandom.Next(2)];
                roundLogger.PutAction(new RoundLog.RoundActivity(null, turn.FieldIndex, RoundLog.RoundAction.RoundBegin));
            }

            return NewGame() || ShipPlacement();
        }

        
        /// <summary>Part of manual round control. Runs a turn</summary>
        /// <returns>True if the round is still ongoing, false if the round is finished</returns>
        /// <seealso cref="RunRound()"/>
        /// <seealso cref="NewRound()"/>
        public bool RoundTurn()
        {
            bool result = GamePlay();
            turn = Opponent(turn);

            if (RoundTurnEndEvent != null)
                RoundTurnEndEvent();

            return result;
        }

        
        /// <summary>Runs an entire round until an opponent wins</summary>
        /// <seealso cref="RoundTurn()"/>
        /// <seealso cref="NewRound()"/>
        public void RunRound()
        {
            while (!RoundTurn() && isRunning) ;
        }

        
        /// <summary>Determines if the number of rounds specified has been reached</summary>
        /// <returns>True if an opponent has reached the number of rounds</returns>
        private bool RoundsReached(int rounds)
        {
            return controllers[0].ControllerInformation.Score >= rounds || controllers[1].ControllerInformation.Score >= rounds;
        }

        
        /// <summary>Returns a dictionary linking the scores to the opponents.</summary>
        /// <returns>A dictionary linking the scores to the opponents.</returns>
        public Dictionary<IBattleshipController, int> GetScores()
        {
            return controllers.ToDictionary(s => s.BattleshipController, s => s.ControllerInformation.Score);
        }

        
        /// <summary>Runs a specified amount of rounds between two opponents.</summary>
        /// <returns>A list of the two opponents with their scores attached.</returns>
        /// <param name="playOut">If the amount of rounds specified is to be played out (ie.
        /// total score between the two opponents) or not (ie. first score to rounds)</param>
        /// <param name="rounds">The amount of rounds to play.</param>
        public void RunRounds(int rounds, bool playOut)
        {
            isRunning = true;
            int gamePlays = 0;

            foreach (Controller bc in controllers)
                bc.NewMatch(Opponent(bc).ToString());

            //This loop continues until this competition is complete.
            while (playOut ? !RoundsReached(rounds) : gamePlays++ < (rounds * 2 - 1) && isRunning)
            {
                NewRound();
                RunRound();
                if (RoundEndEvent != null)
                {
                    RoundEndEvent();
                }
            }

            foreach (Controller controller in controllers)
                controller.MatchOver();
            if (MatchEndEvent != null)
                MatchEndEvent();
            isRunning = false;
        }

        
        /// <summary>Runs the match between two opponents.
        /// Bases the amount of rounds to play and how to play them in the configuration.</summary>
        public void RunCompetition()
        {
            RunRounds(configuration.GetValue<int>("mbc_rounds"),
                configuration.GetValue<bool>("mbc_playout"));
        }

        
        /// <summary>Runs the match between two opponents in a separate thread using the configuration.</summary>
        public void RunCompetitionThread()
        {
            if (isRunning)
            {
                return;
            }
            competitionThread.Start();
        }

        
        /// <summary>Stops rounds from being played anymore.</summary>
        public void StopCompetitionThread()
        {
            isRunning = false;
        }
    }
}
