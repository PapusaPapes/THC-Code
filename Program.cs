using System;
using System.Collections.Generic;
using TeamHeroCoderLibrary;

namespace PlayerCoder
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Connecting...");
            var connectionManager = new GameClientConnectionManager();
            connectionManager.SetExchangePath(MyAI.FolderExchangePath);
            connectionManager.onHeroHasInitiative = MyAI.ProcessAI;
            connectionManager.StartListeningToGameClientForHeroPlayRequests();
        }
    }

    // Team: Rogue / Alchemist / Monk
    // Items: 1 Potion, 2 Ethers, 32 Essence
    // Design principles:
    //   - Every ability wrapped in AreAbilityAndTargetLegal — no softlocks ever
    //   - Always falls back to Attack then Wait — never gets stuck
    //   - Alchemist crafts one item per turn, maintains mana >= 20 always
    //   - Items used reactively when actually needed, not hoarded
    //   - Generic priority lists work across all fights without tuning

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // health thresholds
        private const float HP_CRITICAL = 0.30f;  // use elixir/potion
        private const float HP_LOW      = 0.55f;  // use mega elixir if 2+ allies here
        private const float HP_LIGHT    = 0.75f;  // general low

        // mana thresholds
        private const float MP_LOW      = 0.25f;  // use ether
        private const float MP_ROGUE    = 0.20f;  // rogue needs less mana

        // attack thresholds
        private const float FINISH_HP   = 0.35f;  // finish low HP targets first

        // kill order — Cleric > Alchemist > Wizard > Rogue > Monk > Fighter
        private static readonly HeroJobClass[] KillOrder =
        {
            HeroJobClass.Cleric, HeroJobClass.Alchemist, HeroJobClass.Wizard,
            HeroJobClass.Rogue,  HeroJobClass.Monk,      HeroJobClass.Fighter
        };

        // ============================================================
        // MAIN ROUTER
        // ============================================================
        public static void ProcessAI()
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;
            if (actor == null) return;
            Console.WriteLine($"Actor: {actor.jobClass} HP:{actor.health}/{actor.maxHealth} MP:{actor.mana}/{actor.maxMana}");

            switch (actor.jobClass)
            {
                case HeroJobClass.Rogue:     ControlRogue(actor);     return;
                case HeroJobClass.Alchemist: ControlAlchemist(actor); return;
                case HeroJobClass.Monk:      ControlMonk(actor);      return;
                default:                     Wait(actor);              return;
            }
        }

        // ============================================================
        // ROGUE
        // Priority: emergency items > ether > silence casters/fighters > finish > attack
        // Stealth bypasses Cover so hits any target
        // Item Jockey gives free turn after every item use
        // ============================================================
        private static void ControlRogue(Hero actor)
        {
            // always check emergency items first for Item Jockey procs
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MP_ROGUE)) return;

            // silence priority: Cleric > Alchemist > Wizard > Fighter
            // shuts down healing, crafting, and Brave stacking
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Cleric)))   return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Wizard)))   return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Fighter)))  return;

            // finish low HP targets before moving on
            if (FinishTarget(actor)) return;

            // poison support heroes for chip damage
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Alchemist))) return;

            // stun focus target (skip Fighter if casters are alive)
            Hero stunTarget = BestAttackTarget();
            if (stunTarget != null && stunTarget.jobClass != HeroJobClass.Fighter || !HasCasters())
                if (Act(actor, Ability.StunStrike, stunTarget)) return;

            // attack
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        // ============================================================
        // ALCHEMIST
        // Priority: emergency items > use ether > craft one item > dispel > slow > attack
        // Crafts one item per turn, never drops below 20 mana
        // Fight-aware crafting: Wizard fights get Full Remedy first,
        // physical fights get Elixir first, mixed get Ether first
        // ============================================================
        private static void ControlAlchemist(Hero actor)
        {
            // petrify cleanse only — don't waste mana on Poison/Silence that items handle
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.Cleanse, petrified)) return;

            // use items if team is in trouble
            if (UseEmergencyItem(actor)) return;

            // self-heal before crafting if critically low
            if (HealthRatio(actor) <= HP_CRITICAL)
            {
                if (Act(actor, Ability.Elixir, actor)) return;
                if (Act(actor, Ability.Potion, actor)) return;
            }

            // use ether on mana-needy ally
            if (UseEther(actor, MP_LOW)) return;

            // CRAFT — one item per turn, mana-safe
            // always have Ether available for mana sustain
            if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.Ether))
            { SelfCast(actor, Ability.CraftEther); return; }

            // craft based on fight type
            if (HasWizard())
            {
                // Wizard: Full Remedy first for Doom/Petrify coverage
                if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftFullRemedy); return; }
                if (actor.mana >= 40 && Essence() >= 3 && !HasItem(Ability.Elixir) && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftElixir); return; }
                if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.Potion) && !HasItem(Ability.Elixir) && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftPotion); return; }
                if (actor.mana >= 60 && Essence() >= 4 && !HasItem(Ability.MegaElixir))
                { SelfCast(actor, Ability.CraftMegaElixir); return; }
            }
            else if (!HasCasters())
            {
                // Pure physical: Elixir for self-healing, then Mega Elixir for team
                if (actor.mana >= 40 && Essence() >= 3 && !HasItem(Ability.Elixir))
                { SelfCast(actor, Ability.CraftElixir); return; }
                if (actor.mana >= 60 && Essence() >= 4 && !HasItem(Ability.MegaElixir) && !HasItem(Ability.Elixir))
                { SelfCast(actor, Ability.CraftMegaElixir); return; }
                if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.Potion) && !HasItem(Ability.Elixir) && !HasItem(Ability.MegaElixir))
                { SelfCast(actor, Ability.CraftPotion); return; }
            }
            else
            {
                // Mixed: Full Remedy then Elixir
                if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftFullRemedy); return; }
                if (actor.mana >= 40 && Essence() >= 3 && !HasItem(Ability.Elixir) && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftElixir); return; }
                if (actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.Potion) && !HasItem(Ability.Elixir) && !HasItem(Ability.FullRemedy))
                { SelfCast(actor, Ability.CraftPotion); return; }
                if (actor.mana >= 60 && Essence() >= 4 && !HasItem(Ability.MegaElixir))
                { SelfCast(actor, Ability.CraftMegaElixir); return; }
            }

            // craft revive if someone is dead
            Hero dead = BestDeadAlly();
            if (dead != null && actor.mana >= 20 && Essence() >= 2 && !HasItem(Ability.Revive))
            { SelfCast(actor, Ability.CraftRevive); return; }

            // dispel AutoLife only — never loop-dispel Brave
            if (actor.mana >= 15)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (HasStatus(foe, StatusEffect.AutoLife) && Act(actor, Ability.Dispel, foe)) return;

            // slow priority targets when mana available — casters only
            if (HasCasters() && actor.mana >= 15)
            {
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Monk)))      return;
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Cleric)))    return;
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Alchemist))) return;
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard)))    return;
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Rogue)))     return;
            }

            if (FinishTarget(actor)) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            // guaranteed fallback
            Wait(actor);
        }

        // ============================================================
        // MONK
        // Priority: emergency items > ether > debrave/defaith > flurry > finish > attack
        // Flurry bypasses Cover and hits all enemies simultaneously
        // ============================================================
        private static void ControlMonk(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MP_ROGUE)) return;

            // vs pure physical — Flurry first to damage all before Brave stacks
            if (!HasCasters())
            {
                Hero ft = BestAttackTarget();
                if (ft != null && Act(actor, Ability.FlurryOfBlows, ft)) return;
                if (SelfCast(actor, Ability.FlurryOfBlows)) return;
            }

            // debrave physical threats
            if (Act(actor, Ability.Debrave, FindDebrave(HeroJobClass.Monk)))    return;
            if (Act(actor, Ability.Debrave, FindDebrave(HeroJobClass.Fighter))) return;
            if (Act(actor, Ability.Debrave, FindDebrave(HeroJobClass.Rogue)))   return;

            // defaith magic threats
            if (Act(actor, Ability.Defaith, FindDefaith(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.Defaith, FindDefaith(HeroJobClass.Wizard)))    return;
            if (Act(actor, Ability.Defaith, FindDefaith(HeroJobClass.Alchemist))) return;

            if (FinishTarget(actor)) return;

            // flurry when 3+ enemies alive
            if (CountEnemies() >= 3)
            {
                Hero ft = BestAttackTarget();
                if (ft != null && Act(actor, Ability.FlurryOfBlows, ft)) return;
                if (SelfCast(actor, Ability.FlurryOfBlows)) return;
            }

            // chakra to restore mana for allies
            if (actor.mana > 10 && NeedsChakra())
                if (Act(actor, Ability.Chakra, actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        // ============================================================
        // SHARED ITEM USAGE
        // ============================================================
        private static bool UseEmergencyItem(Hero actor)
        {
            // petrify — most urgent, no initiative for 3 turns
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.PetrifyRemedy, petrified)) return true;
            if (petrified != null && Act(actor, Ability.FullRemedy,    petrified)) return true;

            // doom — 3 turn death timer
            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null && Act(actor, Ability.FullRemedy, doomed)) return true;

            // silence on Alchemist or Rogue — they need mana abilities
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HasStatus(ally, StatusEffect.Silence) &&
                    (ally.jobClass == HeroJobClass.Alchemist || ally.jobClass == HeroJobClass.Rogue))
                {
                    if (Act(actor, Ability.SilenceRemedy, ally)) return true;
                    if (Act(actor, Ability.FullRemedy,    ally)) return true;
                }

            // mega elixir when 2+ allies seriously hurt
            if (CountBelow(HP_LOW) >= 2 && SelfCast(actor, Ability.MegaElixir)) return true;

            // revive dead ally
            Hero dead = BestDeadAlly();
            if (dead != null && Act(actor, Ability.Revive, dead)) return true;

            // elixir/potion on critically low ally
            Hero lowest = LowestAlly();
            if (lowest != null && HealthRatio(lowest) <= HP_CRITICAL)
            {
                if (Act(actor, Ability.Elixir, lowest)) return true;
                if (Act(actor, Ability.Potion, lowest)) return true;
            }

            return false;
        }

        private static bool UseEther(Hero actor, float threshold)
        {
            Hero target = null; float lowestMana = threshold + 0.001f;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float r = ManaRatio(ally);
                if (r < lowestMana) { lowestMana = r; target = ally; }
            }
            return target != null && Act(actor, Ability.Ether, target);
        }

        // ============================================================
        // ATTACK HELPERS
        // ============================================================
        private static bool FinishTarget(Hero actor)
        {
            Hero t = BestAttackTarget();
            return t != null && HealthRatio(t) <= FINISH_HP && Act(actor, Ability.Attack, t);
        }

        private static Hero BestAttackTarget()
        {
            // first try without Cover bypass (respects Cover for non-attack abilities)
            foreach (HeroJobClass jc in KillOrder)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (foe.jobClass == jc && Legal(Ability.Attack, foe)) return foe;
            // fallback: ignore Cover — attacking into Cover still deals damage to the Fighter
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (LegalIgnoreCover(Ability.Attack, foe)) return foe;
            return null;
        }

        // ============================================================
        // TARGET FINDERS
        // ============================================================
        private static Hero FindUnsilenced(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Silence) && Legal(Ability.SilenceStrike, foe))
                    return foe;
            return null;
        }

        private static Hero FindUnpoisoned(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Poison) && Legal(Ability.PoisonStrike, foe))
                    return foe;
            return null;
        }

        private static Hero FindUnslowed(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Slow) && Legal(Ability.Slow, foe))
                    return foe;
            return null;
        }

        private static Hero FindDebrave(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Debrave) && Legal(Ability.Debrave, foe))
                    return foe;
            return null;
        }

        private static Hero FindDefaith(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Defaith) && Legal(Ability.Defaith, foe))
                    return foe;
            return null;
        }

        private static Hero FindAllyWithStatus(params StatusEffect[] statuses)
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;
            Hero best = null; float bestScore = float.MinValue;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                // never return the actor itself — prevents Alchemist cleansing itself when not afflicted
                if (ally == actor) continue;
                if (!HasAnyStatus(ally, statuses)) continue;
                float score = AllyValue(ally) + (1f - HealthRatio(ally)) * 100f;
                if (score > bestScore) { bestScore = score; best = ally; }
            }
            return best;
        }

        private static Hero LowestAlly()
        {
            Hero best = null; float lowest = float.MaxValue;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float r = HealthRatio(ally);
                if (r < lowest) { lowest = r; best = ally; }
            }
            return best;
        }

        private static Hero BestDeadAlly()
        {
            Hero best = null; float bestScore = float.MinValue;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health > 0) continue;
                float score = AllyValue(ally);
                if (score > bestScore) { bestScore = score; best = ally; }
            }
            return best;
        }

        private static Hero FindLivingEnemy(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc) return foe;
            return null;
        }

        // ============================================================
        // SITUATION DETECTION
        // ============================================================
        private static bool HasWizard()  => FindLivingEnemy(HeroJobClass.Wizard) != null;
        private static bool HasCasters() =>
            FindLivingEnemy(HeroJobClass.Cleric)    != null ||
            FindLivingEnemy(HeroJobClass.Alchemist) != null ||
            FindLivingEnemy(HeroJobClass.Wizard)    != null;

        private static bool NeedsChakra()
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (ManaRatio(ally) <= 0.15f) return true;
            return false;
        }

        private static int CountEnemies()
        {
            int c = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes)) c++;
            return c;
        }

        private static int CountBelow(float threshold)
        {
            int c = 0;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HealthRatio(ally) <= threshold) c++;
            return c;
        }

        // ============================================================
        // ITEM DETECTION
        // ============================================================
        private static bool HasItem(Ability ability)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (Utility.AreAbilityAndTargetLegal(ability, ally, false)) return true;
            return false;
        }

        private static int Essence() => TeamHeroCoder.BattleState.allyEssenceCount;

        // ============================================================
        // CORE ACTION HELPERS — all wrapped in legality checks
        // ============================================================
        private static bool Act(Hero actor, Ability ability, Hero target)
        {
            if (actor == null || target == null) return false;
            if (!Utility.AreAbilityAndTargetLegal(ability, target, false)) return false;
            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
            return true;
        }

        private static bool SelfCast(Hero actor, Ability ability)
        {
            if (actor == null) return false;
            if (!Utility.AreAbilityAndTargetLegal(ability, actor, false)) return false;
            Console.WriteLine($"{actor.jobClass} uses {ability}");
            TeamHeroCoder.PerformHeroAbility(ability, actor);
            return true;
        }

        private static bool Legal(Ability ability, Hero target) =>
            target != null && Utility.AreAbilityAndTargetLegal(ability, target, false);

        // use this for attacks — bypasses Cover check so we can always hit someone
        private static bool LegalIgnoreCover(Ability ability, Hero target) =>
            target != null && Utility.AreAbilityAndTargetLegal(ability, target, true);

        private static void Wait(Hero actor)
        {
            Console.WriteLine($"{actor.jobClass} waits");
            TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor);
        }

        // ============================================================
        // UTILITY
        // ============================================================
        private static IEnumerable<Hero> Living(IEnumerable<Hero> heroes)
        {
            foreach (Hero h in heroes)
                if (h.health > 0) yield return h;
        }

        private static float HealthRatio(Hero h) =>
            h == null || h.maxHealth <= 0 ? 1f : (float)h.health / h.maxHealth;

        private static float ManaRatio(Hero h) =>
            h == null || h.maxMana <= 0 ? 1f : (float)h.mana / h.maxMana;

        private static float AllyValue(Hero h)
        {
            float s = 0f;
            if (h.jobClass == HeroJobClass.Alchemist) s += 340f;
            if (h.jobClass == HeroJobClass.Monk)      s += 300f;
            if (h.jobClass == HeroJobClass.Rogue)     s += 280f;
            return s + h.speed * 2f + h.physicalAttack * 1.8f + h.special * 1.5f;
        }

        private static bool HasStatus(Hero h, StatusEffect s)
        {
            if (h == null) return false;
            foreach (var e in h.statusEffectsAndDurations)
                if (e.statusEffect == s) return true;
            return false;
        }

        private static bool HasAnyStatus(Hero h, params StatusEffect[] statuses)
        {
            foreach (var s in statuses)
                if (HasStatus(h, s)) return true;
            return false;
        }
    }
}