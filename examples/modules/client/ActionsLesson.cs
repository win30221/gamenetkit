using System;
using Realtime.Actions;

namespace Realtime.Examples.Modules
{
    public static class ActionsLesson
    {
        public static void Main()
        {
            var actions = new ActionPresentation(timeout: .5, retention: 2);
            int effects = 0;
            if (actions.Begin(1, 0)) effects++; // Application displays a provisional effect.
            if (actions.Begin(1, .1)) effects++; // Same action cannot display twice.
            Check.That(actions.Resolve(1, true) && actions.WasPresented(1), "confirmed effect is already visible");
            actions.Begin(2, .1); actions.Resolve(2, false); // Application cancels effect 2.
            actions.Begin(3, .2); actions.Advance(1); // Expiry also needs UI cleanup.
            Check.That(effects==1 && actions.Confirmed==1 && actions.Rejected==1 && actions.Expired==1, "action accounting");
            Console.WriteLine("actions: confirmed-only waits; speculative displays once immediately, confirmed=1 rejected=1 expired=1; damage remains server-owned");
            actions.Clear(); Check.That(actions.Pending==0 && !actions.WasPresented(1), "reset ledger");
        }
    }
}
