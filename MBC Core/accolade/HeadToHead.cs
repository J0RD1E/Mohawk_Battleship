﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MBC.Core.mbc.accolade
{
    public class HeadToHead : AccoladeProcessor
    {
        static HeadToHead()
        {
            Configuration.Default.SetValue<int>("accolade_h2h_diff", 4);
        }
        int cnt = 0;
        int diff = 0;
        IBattleshipController op = null;

        private void ResetCounters()
        {
            cnt = 0;
            diff = 0;
        }

        public RoundLog.RoundAccolade Process(RoundLog.RoundActivity a)
        {
            if (a.action != RoundLog.RoundAction.ShotAndHit)
                return RoundLog.RoundAccolade.None;

            if (op == a.ibc)
            {
                diff++;
                if (diff > Configuration.Global.GetValue<int>("accolade_h2h_diff"))
                    ResetCounters();
            }
            else
            {
                op = a.ibc;
                diff = 0;
                cnt++;
            }

            if (cnt > Configuration.Global.GetValue<int>("accolade_h2h_count"))
            {
                ResetCounters();
                return RoundLog.RoundAccolade.HeadToHead;
            }

            return RoundLog.RoundAccolade.None;
        }
    }
}