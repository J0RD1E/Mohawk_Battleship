﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MBC.Core.Events
{
    public class MatchRoundEvent : MatchEvent
    {
        private List<RoundEvent> roundEvents;
    }
}