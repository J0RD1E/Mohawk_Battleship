﻿namespace MBC.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Threading;

    /**
     * <summary>A Competition object contains game logic for a game of battleship.
     * 
     * A competition has 3 events that are called internally during game play:
     * 
     *      RoundTurnEndEvent - Called when a controller has ended their turn.
     *      RoundEndEvent - Called when a round has ended.
     *      MatchEndEvent - Called when a matchup has ended (from any methods invoking RunRounds)
     *      
     * The competition can be run either in the same thread as the calling method (RunCompetition()), or
     * in a different thread (RunCompetitionThread()), which can be stopped at any time (StopCompetitionThread()).
     * By using one of these methods, the Competition object will use the Configuration object that it has
     * been constructed with to determine the number of rounds to play. Using RunRounds(int, bool) will run
     * a custom number of rounds in the same thread, but use of this method is not recommended.
     * 
     * The competition can be run in a turn-by-turn manner by utilizing three methods:
     * 
     *      NewRound() - Starts a new round in the competition. Finishes any ongoing round.
     *      RoundTurn() - Run a turn in the round.
     *      RoundRun() - Run through remaining turns in the round.
     *      
     * When constructing a new Competition, keep in mind that the order of the array of IBattleshipControllers
     * does not change, even in the Field object that is generated.
     * </summary>
     */
    public class Competition
    {
        /**
         * <summary>Sets default configuration values for keys that relate to this class.
         * Should be called before using the global Configuration.Default object.</summary>
         */
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
        private Configuration config;            //Configuration used for this competition
        private Field fieldInfo;              //Contains field information
        private List<RoundLog> roundList;
        private RoundLog roundLogger;               //The current RoundLog for the current round

        private Thread compThread;                  //The thread used for tasking this competition 
        private bool isRunning = false;             //Should be false whenever rounds should stop running.

        private Controller turn;            //The current opponent's turn

        public delegate void RndTick();
        public event RndTick RoundTurnEndEvent;

        public delegate void RndEnd();
        public event RndEnd RoundEndEvent;

        public delegate void MatchEnd();
        public event MatchEnd MatchEndEvent;


        public Controller Turn { get { return turn; } }

        /**
         * <summary>Constructs a new BattleshipCompetition object using a BattleshipConfig for configuration.</summary>
         */
        public Competition(IBattleshipController[] ibc, Configuration config)
        {
            init(ibc, config, config.GetValue<int>("mbc_random_seed"));
        }

        /**
         * <summary>Constructs a new BattleshipCompetition object using a BattleshipConfig for configuration.
         * Also, a seed value can be specified.</summary>
         */
        public Competition(IBattleshipController[] ibc, Configuration config, int seedNum)
        {
            init(ibc, config, seedNum); 
        }

        /**
         * <summary>Called only by the constructor, does initialization of this class.</summary>
         */
        private void init(IBattleshipController[] ibc, Configuration conf, int seedNum)
        {
            config = conf;
            fieldInfo = new Field(ibc);
            fieldInfo.fixedRandom = new Random(seedNum);
            fieldInfo.gameSize = new Size(config.GetValue<int>("field_width"), config.GetValue<int>("field_height"));
            fieldInfo.shipSizes = config.GetConfigValueArray<int>("field_ship_sizes");
            fieldInfo.timeoutLimit = new TimeSpan(0, 0, 0, 0, config.GetValue<int>("timeout_millis"));

            roundList = new List<RoundLog>();

            controllers = new Controller[2];
            controllers[0] = new Controller(ibc[0], fieldInfo, 0);
            controllers[1] = new Controller(ibc[1], fieldInfo, 1);

            compThread = new Thread(new ThreadStart(RunCompetition));
        }

        /**
         * <returns>The battlefield associated with this competition</returns>
         */
        public Field GetBattlefield()
        {
            return fieldInfo;
        }

        /**
         * <returns>The number of rounds played, complete or incomplete</returns>
         */
        public int GetRoundCount()
        {
            return roundList.Count;
        }

        /**
         * <returns>The log for the round #idx</returns>
         */
        public RoundLog GetRoundLogAt(int idx)
        {
            lock(roundList) {
                if (idx < 0 || idx > roundList.Count)
                    return null;
                return roundList.ElementAt(idx);
            }
        }

        /**
         * <summary>Creates a list of new ships to be placed</summary>
         * <returns>A list of ships not placed on the board, defined to having a length specified by shipSizes</returns>
         */
        private List<Ship> GenerateNewShips()
        {
            return (from s in fieldInfo.shipSizes
                    select new Ship(s)).ToList();
        }

        /**
         * <summary>Used to notify both players whether they won or lost.</summary>
         * <returns>Always returns true</returns>
         */
        private bool GameResultPush(Controller winner, Controller loser, string reason)
        {
            winner.GameWon();
            loser.GameLost();
            roundLogger.PutAction(new RoundLog.RoundActivity(null, winner.FieldIDX, RoundLog.RoundAction.Won));
            roundLogger.PutAction(new RoundLog.RoundActivity(null, loser.FieldIDX, RoundLog.RoundAction.Lost));
            roundLogger.PutAction(new RoundLog.RoundActivity(reason, -1, RoundLog.RoundAction.RoundEnd));
            return true;
        }

        /**
         * <returns>The opposing BattleshipOpponent relative to 'ibc'</returns>
         */
        private Controller Opponent(Controller ibc)
        {
            foreach (Controller bc in controllers)
                if (ibc != bc)
                    return bc;
            return null;
        }

        /**
         * <summary>Notifys each player that a new game is commencing</summary>
         * <returns>True if the game is over, false if the game is still on-going.</returns>
         */
        private bool NewGame()
        {
            foreach (Controller bc in controllers)
                if (bc.NewGame())
                    return GameResultPush(Opponent(bc), bc, RoundLog.RoundActivity.Reason_Timeout);
            return false;
        }

        /**
         * <summary>Ship placement mode for the players. Repeats until ships have been placed validly</summary>
         * <returns>True if the game is over. False if the game is on-going</returns>
         */
        private bool ShipPlacement()
        {
            foreach (Controller bc in controllers)
                do
                {
                    if (bc.PlaceShips(GenerateNewShips()))
                        return GameResultPush(Opponent(bc), bc, RoundLog.RoundActivity.Reason_Timeout);
                    RoundLog.RoundActivity action = new RoundLog.RoundActivity(bc.ShipsReady() ? "Ready" : "Invalid", bc.FieldIDX, RoundLog.RoundAction.ShipsPlaced);
                    action.fieldState = new Field(fieldInfo);
                    roundLogger.PutAction(action);
                } while (!bc.ShipsReady());
            return false;
        }

        /**
         * <summary>The main game logic of battleship.</summary>
         * <returns>True if the game is over (the method never returns false)</returns>
         */
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
                roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIDX, RoundLog.RoundAction.ShotAndHit, turn.GetTimeTaken()));

                bool sunk = shipHit.IsSunk(turn.GetFieldInfo().shotsMade);

                turn.ShotHit(shot, sunk);
                if (sunk)
                    roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIDX, RoundLog.RoundAction.ShipDestroyed, turn.GetTimeTaken()));
            }
            else
            {
                roundLogger.PutAction(new RoundLog.RoundActivity(shot.X + "," + shot.Y, turn.FieldIDX, RoundLog.RoundAction.ShotAndMiss, turn.GetTimeTaken()));
                turn.ShotMiss(shot);
            }

            //Are there any ships left from the other opponent?
            if (!Opponent(turn).IsAlive(turn.GetFieldInfo().shotsMade))
                return GameResultPush(turn, Opponent(turn), null);
            return false;
        }

        /**
         * <summary>Part of manual round control. Clears the previous round if it exists
         * and starts a new round</summary>
         * <returns>True if the round is ongoing, false if it has been won</returns>
         */
        public bool NewRound()
        {
            lock (roundList)
            {
                if (roundLogger != null && roundLogger.GetAt(roundLogger.GetActivityCount()).action != RoundLog.RoundAction.RoundEnd)
                    roundLogger.PutAction(new RoundLog.RoundActivity("Round ended before win", -1, RoundLog.RoundAction.RoundEnd));

                roundLogger = new RoundLog();
                roundList.Add(roundLogger);

                turn = controllers[fieldInfo.fixedRandom.Next(2)];
                roundLogger.PutAction(new RoundLog.RoundActivity(null, turn.FieldIDX, RoundLog.RoundAction.RoundBegin));
            }

            return NewGame() || ShipPlacement();
        }

        /**
         * <summary>Part of manual round control. Runs a turn</summary>
         * <returns>True if the round is still ongoing, false if the round is finished</returns>
         */
        public bool RoundTurn()
        {
            bool res = GamePlay();
            turn = Opponent(turn);

            if (RoundTurnEndEvent != null)
                RoundTurnEndEvent();

            return res;
        }

        /**
         * <summary>Runs an entire round until an opponent wins</summary>
         */
        public void RunRound()
        {
            while (!RoundTurn() && isRunning) ;
        }

        /**
         * <summary>Determines if the number of rounds specified has been reached</summary>
         * <returns>True if an opponent has reached the number of rounds</returns>
         */
        private bool RoundsReached(int rnds)
        {
            return controllers[0].GetFieldInfo().score >= rnds || controllers[1].GetFieldInfo().score >= rnds;
        }

        /**
         * <summary>Returns a dictionary linking the scores to the opponents.</summary>
         */
        public Dictionary<IBattleshipController, int> GetScores()
        {
            return controllers.ToDictionary(s => s.ibc, s => s.GetFieldInfo().score);
        }

        /**
         * <summary>Runs a specified amount of rounds between two opponents.</summary>
         * <returns>A list of the two opponents with their scores attached.</returns>
         * <param name="playOut">If the amount of rounds specified is to be played out (ie.
         * total score between the two opponents) or not (ie. first score to rounds)</param>
         * <param name="rnds">The amount of rounds to play.</param>
         */
        public void RunRounds(int rnds, bool playOut)
        {
            isRunning = true;
            int gamePlays = 0;

            foreach (Controller bc in controllers)
                bc.NewMatch(Opponent(bc).ToString());

            //This loop continues until this competition is complete.
            while (playOut ? !RoundsReached(rnds) : gamePlays++ < (rnds * 2 - 1) && isRunning)
            {
                RunRound();
                if (RoundEndEvent != null)
                    RoundEndEvent();
            }

            foreach (Controller bc in controllers)
                bc.MatchOver();
            if (MatchEndEvent != null)
                MatchEndEvent();
        }

        /**
         * <summary>Runs the match between two opponents.
         * Bases the amount of rounds to play and how to play them in the configuration.</summary>
         */
        public void RunCompetition()
        {
            RunRounds(config.GetValue<int>("competition_rounds"),
                config.GetValue<bool>("competition_mode_playout"));
        }

        /**
         * <summary>Runs the match between two opponents in a separate thread using the configuration.</summary>
         */
        public void RunCompetitionThread()
        {
            if (isRunning)
                return;
            compThread.Start();
        }

        /**
         * <summary>Stops rounds from being played anymore.</summary>
         */
        public void StopCompetitionThread()
        {
            isRunning = false;
        }

        public Controller GetController(int index)
        {
            if (index < controllers.Length - 1)
            {
                return controllers[index];
            }

            return null;
        }
    }
}
