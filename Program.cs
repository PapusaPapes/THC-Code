using TeamHeroCoderLibrary;

namespace PlayerCoder
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Connecting...");
            GameClientConnectionManager connectionManager;
            connectionManager = new GameClientConnectionManager();
            connectionManager.SetExchangePath(MyAI.FolderExchangePath);
            connectionManager.onHeroHasInitiative = MyAI.ProcessAI;
            connectionManager.StartListeningToGameClientForHeroPlayRequests();
        }
    }

    // My team: Rogue / Alchemist / Cleric 
    // Who to silence first: Alchemist > Wizard > Cleric > Fighter > Monk > Rogue
    // Who to poison first:  Monk > Fighter > Rogue > Cleric > Wizard > Alchemist
    // Who to slow first:    Monk > Fighter > Rogue > Wizard > Cleric > Alchemist
    // Who to haste first:   Rogue > Monk > Fighter > Alchemist > Cleric > Wizard
    // Who to faith first:   Cleric > Alchemist > Wizard > Fighter > Rogue > Monk

    public static class MyAI
    {
        public static string FolderExchangePath = "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // tracking crafted items 
        public static int PotionCount = 0;
        public static int EtherCount = 0;
        public static bool CounterIsActive = false;
        public static bool EnemyHasSilenceRemedy = false;

        static readonly HeroJobClass[] SilencePriority = {
            HeroJobClass.Alchemist, HeroJobClass.Wizard, HeroJobClass.Cleric,
            HeroJobClass.Fighter,   HeroJobClass.Monk,   HeroJobClass.Rogue,
        };
        static readonly HeroJobClass[] PoisonPriority = {
            HeroJobClass.Monk,    HeroJobClass.Fighter, HeroJobClass.Rogue,
            HeroJobClass.Cleric,  HeroJobClass.Wizard,  HeroJobClass.Alchemist,
        };
        static readonly HeroJobClass[] SlowPriority = {
            HeroJobClass.Monk,    HeroJobClass.Fighter, HeroJobClass.Rogue,
            HeroJobClass.Wizard,  HeroJobClass.Cleric,  HeroJobClass.Alchemist,
        };
        static readonly HeroJobClass[] HastePriority = {
            HeroJobClass.Rogue,     HeroJobClass.Monk,    HeroJobClass.Fighter,
            HeroJobClass.Alchemist, HeroJobClass.Cleric,  HeroJobClass.Wizard,
        };
        static readonly HeroJobClass[] FaithPriority = {
            HeroJobClass.Cleric,    HeroJobClass.Alchemist, HeroJobClass.Wizard,
            HeroJobClass.Fighter,   HeroJobClass.Rogue,     HeroJobClass.Monk,
        };

        static public void ProcessAI()
        {
            var actor = TeamHeroCoder.BattleState.heroWithInitiative;
            Console.WriteLine($"Processing AI! Actor: {actor.jobClass} HP:{actor.health}/{actor.maxHealth} MP:{actor.mana}");

            SyncPotionCount();


            if (BossCounterIsActive())
            {
                bool safeTargetExists = GetBestNonCounterTarget() != null;
                if (actor.jobClass == HeroJobClass.Rogue && !safeTargetExists)
                {
                    Console.WriteLine($"Rogue waits — boss counter active, no safe targets");
                    TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor);
                    return;
                }
            }

            if (actor.jobClass == HeroJobClass.Rogue)
                HandleRogue(actor);
            else if (actor.jobClass == HeroJobClass.Cleric)
                HandleCleric(actor);
            else if (actor.jobClass == HeroJobClass.Alchemist)
                HandleAlchemist(actor);
            else
                FallbackAttack(actor);
        }

        // returns true if the enemy is a regular class 
        static bool IsStandardClass(Hero hero)
        {
            return hero.jobClass == HeroJobClass.Fighter
                || hero.jobClass == HeroJobClass.Wizard
                || hero.jobClass == HeroJobClass.Cleric
                || hero.jobClass == HeroJobClass.Rogue
                || hero.jobClass == HeroJobClass.Monk
                || hero.jobClass == HeroJobClass.Alchemist;
        }

        // finds the best enemy to hit during counter window 
        static Hero GetBestNonCounterTarget()
        {
            Hero best = null; float bestScore = -999999f;
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
            {
                if (foe.health <= 0 || !IsStandardClass(foe)) continue;
                float score = GetEnemyThreatScore(foe)
                            + (1f - GetHealthRatio(foe)) * 150f
                            - foe.physicalDefense * 1.2f;
                if (HasStatus(foe, StatusEffect.Poison)) score += 50f;
                if (score > bestScore) { bestScore = score; best = foe; }
            }
            return best;
        }

        // ROGUE
        // heal self if low, use ether if out of mana, silence casters, poison everyone, attack
        static void HandleRogue(Hero actor)
        {
            Console.WriteLine("Rogue branch");

            if (PotionCount > 0 && GetHealthRatio(actor) <= 0.40f)
            {
                Console.WriteLine("Rogue uses Potion on self");
                TeamHeroCoder.PerformHeroAbility(Ability.Potion, actor);
                PotionCount = Math.Max(0, PotionCount - 1);
                return;
            }

            if (EtherCount > 0 && GetManaRatio(actor) <= 0.30f)
            {
                Console.WriteLine("Rogue uses Ether on self");
                TeamHeroCoder.PerformHeroAbility(Ability.Ether, actor);
                EtherCount = Math.Max(0, EtherCount - 1);
                return;
            }

            // don't bother silencing if silence remedies, 
            if (actor.mana >= 15 && !EnemyHasSilenceRemedy)
            {
                Hero target = GetPriorityFoeTarget(SilencePriority, StatusEffect.Silence, reapplyBelowDuration: 0);
                if (target != null && IsStandardClass(target))
                {
                    Console.WriteLine($"Rogue uses SilenceStrike on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.SilenceStrike, target);
                    return;
                }
            }

            // don't poison during counter window
            if (actor.mana >= 15)
            {
                Hero target = GetPriorityFoeTarget(PoisonPriority, StatusEffect.Poison, reapplyBelowDuration: 1);
                if (target != null && (!CounterIsActive || IsStandardClass(target)))
                {
                    Console.WriteLine($"Rogue uses PoisonStrike on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.PoisonStrike, target);
                    return;
                }
            }

            // avoid hitting if counter is up
            Hero attackTarget = CounterIsActive ? GetBestNonCounterTarget() : GetBestPhysicalTarget();
            if (attackTarget != null)
            {
                Console.WriteLine($"Rogue attacks {attackTarget.jobClass}");
                TeamHeroCoder.PerformHeroAbility(Ability.Attack, attackTarget);
                return;
            }

            FallbackAttack(actor);
        }

        // CLERIC
        // res > cleanse > heal > autolife > faith > haste > attack
        static void HandleCleric(Hero actor)
        {
            Console.WriteLine("Cleric branch");

            if (actor.mana >= 25)
            {
                Hero target = GetBestDeadAllyToRevive();
                if (target != null)
                {
                    Console.WriteLine($"Cleric uses Resurrection on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Resurrection, target);
                    return;
                }
            }

            Hero cleanseTarget = GetBestCleanseTarget();
            if (cleanseTarget != null)
            {
                Console.WriteLine($"Cleric uses QuickCleanse on {cleanseTarget.jobClass}");
                TeamHeroCoder.PerformHeroAbility(Ability.QuickCleanse, cleanseTarget);
                return;
            }

            Hero lowestAlly = GetLowestHealthAlly();
            if (lowestAlly != null)
            {
                float ratio = GetHealthRatio(lowestAlly);

                // if out of mana and someone is critical, use a potion
                if (ratio <= 0.30f && actor.mana < 15 && PotionCount > 0)
                {
                    Console.WriteLine($"Cleric uses Potion on {lowestAlly.jobClass} (no mana)");
                    TeamHeroCoder.PerformHeroAbility(Ability.Potion, lowestAlly);
                    PotionCount = Math.Max(0, PotionCount - 1);
                    return;
                }
                if (ratio <= 0.30f && actor.mana >= 15)
                {
                    Console.WriteLine($"Cleric uses QuickHeal on {lowestAlly.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.QuickHeal, lowestAlly);
                    return;
                }
                if (ratio <= 0.55f && actor.mana >= 20)
                {
                    Console.WriteLine($"Cleric uses CureSerious on {lowestAlly.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.CureSerious, lowestAlly);
                    return;
                }
                if (ratio <= 0.70f && actor.mana >= 10)
                {
                    Console.WriteLine($"Cleric uses CureLight on {lowestAlly.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.CureLight, lowestAlly);
                    return;
                }
            }

            // rogue gets autolife first 
            if (actor.mana >= 25)
            {
                Hero target = GetBestAutoLifeTarget();
                if (target != null)
                {
                    Console.WriteLine($"Cleric uses AutoLife on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.AutoLife, target);
                    return;
                }
            }

            if (actor.mana >= 15)
            {
                Hero target = GetPriorityAllyTarget(FaithPriority, StatusEffect.Faith, reapplyBelowDuration: 1);
                if (target != null)
                {
                    Console.WriteLine($"Cleric uses Faith on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Faith, target);
                    return;
                }
            }

            if (actor.mana >= 15)
            {
                Hero target = GetPriorityAllyTarget(HastePriority, StatusEffect.Haste, reapplyBelowDuration: 1);
                if (target != null)
                {
                    Console.WriteLine($"Cleric uses Haste on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Haste, target);
                    return;
                }
            }

            Hero attackTarget = CounterIsActive ? GetBestNonCounterTarget() : GetBestPhysicalTarget();
            if (attackTarget != null)
            {
                Console.WriteLine($"Cleric attacks {attackTarget.jobClass}");
                TeamHeroCoder.PerformHeroAbility(Ability.Attack, attackTarget);
                return;
            }
            if (CounterIsActive) { TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor); return; }
            FallbackAttack(actor);
        }

        // ALCHEMIST
        // craft ether > use ether > use potion if needed > craft potion > dispel > slow > cleanse > haste > attack
        static void HandleAlchemist(Hero actor)
        {
            Console.WriteLine("Alchemist branch");

            // craft ether whenever we run out
            if (actor.mana >= 10 && EtherCount == 0 && CountEssenceInInventory() >= 2)
            {
                Console.WriteLine($"Alchemist crafts Ether (Essence: {CountEssenceInInventory()})");
                TeamHeroCoder.PerformHeroAbility(Ability.CraftEther, actor);
                EtherCount += 1;
                return;
            }

            // use ether on whoever is lowest on mana
            if (EtherCount > 0)
            {
                Hero target = GetLowestManaAlly(0.50f);
                if (target != null)
                {
                    Console.WriteLine($"Alchemist uses Ether on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Ether, target);
                    EtherCount = Math.Max(0, EtherCount - 1);
                    return;
                }
            }

            // use potions before crafting more
            if (PotionCount > 0)
            {
                Hero potionTarget = GetLowestHealthAlly();
                if (potionTarget != null && GetHealthRatio(potionTarget) <= 0.55f)
                {
                    Console.WriteLine($"Alchemist uses Potion on {potionTarget.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Potion, potionTarget);
                    PotionCount = Math.Max(0, PotionCount - 1);
                    return;
                }
            }

            // craft potions when team is hurting and we have essence
            if (actor.mana >= 10 && PotionCount <= 0 && TeamNeedsPotion() && CountEssenceInInventory() >= 2)
            {
                Console.WriteLine($"Alchemist crafts Potion (Essence: {CountEssenceInInventory()})");
                TeamHeroCoder.PerformHeroAbility(Ability.CraftPotion, actor);
                PotionCount = 2;
                return;
            }

            if (actor.mana >= 15)
            {
                Hero target = GetBestDispelTarget();
                if (target != null)
                {
                    Console.WriteLine($"Alchemist uses Dispel on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Dispel, target);
                    return;
                }
            }

            if (actor.mana >= 15)
            {
                Hero target = GetPriorityFoeTarget(SlowPriority, StatusEffect.Slow, reapplyBelowDuration: 1);
                if (target != null)
                {
                    Console.WriteLine($"Alchemist uses Slow on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Slow, target);
                    return;
                }
            }

            if (actor.mana >= 15)
            {
                Hero target = GetBestCleanseTarget();
                if (target != null)
                {
                    Console.WriteLine($"Alchemist uses Cleanse on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Cleanse, target);
                    return;
                }
            }

            if (actor.mana >= 15)
            {
                Hero target = GetPriorityAllyTarget(HastePriority, StatusEffect.Haste, reapplyBelowDuration: 1);
                if (target != null)
                {
                    Console.WriteLine($"Alchemist uses Haste on {target.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Haste, target);
                    return;
                }
            }

            Hero attackTarget = CounterIsActive ? GetBestNonCounterTarget() : GetBestPhysicalTarget();
            if (attackTarget != null)
            {
                Console.WriteLine($"Alchemist attacks {attackTarget.jobClass}");
                TeamHeroCoder.PerformHeroAbility(Ability.Attack, attackTarget);
                return;
            }
            if (CounterIsActive) { TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor); return; }
            FallbackAttack(actor);
        }

        // walks through a priority list and returns the first enemy whose status is expired
        static Hero GetPriorityFoeTarget(HeroJobClass[] priority, StatusEffect status, int reapplyBelowDuration = 0)
        {
            foreach (HeroJobClass jobClass in priority)
                foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
                    if (foe.health > 0 && foe.jobClass == jobClass
                        && GetStatusDuration(foe, status) <= reapplyBelowDuration)
                        return foe;

            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
                if (foe.health > 0 && GetStatusDuration(foe, status) <= reapplyBelowDuration)
                    return foe;

            return null;
        }

        // same but for allies
        static Hero GetPriorityAllyTarget(HeroJobClass[] priority, StatusEffect status, int reapplyBelowDuration = 0)
        {
            foreach (HeroJobClass jobClass in priority)
                foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                    if (ally.health > 0 && ally.jobClass == jobClass
                        && GetStatusDuration(ally, status) <= reapplyBelowDuration)
                        return ally;

            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && GetStatusDuration(ally, status) <= reapplyBelowDuration)
                    return ally;

            return null;
        }

        static int GetStatusDuration(Hero hero, StatusEffect status)
        {
            if (hero == null) return 0;
            foreach (StatusEffectAndDuration se in hero.statusEffectsAndDurations)
                if (se.statusEffect == status) return se.duration;
            return 0;
        }

        // rogue gets autolife first, then whoever scores highest
        static Hero GetBestAutoLifeTarget()
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && ally.jobClass == HeroJobClass.Rogue
                    && !HasStatus(ally, StatusEffect.AutoLife))
                    return ally;

            Hero best = null; float bestScore = -999999f;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && !HasStatus(ally, StatusEffect.AutoLife))
                { float s = GetAllyValueScore(ally); if (s > bestScore) { bestScore = s; best = ally; } }
            return best;
        }

        // petrify is the worst, then silence, then poison
        static Hero GetBestCleanseTarget()
        {
            Hero best = null; float bestScore = -1f;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health <= 0) continue;
                float score = 0f;
                if (HasStatus(ally, StatusEffect.Petrifying)) score += 200f;
                if (HasStatus(ally, StatusEffect.Silence))    score += 120f;
                if (HasStatus(ally, StatusEffect.Poison))     score += 80f;
                if (score <= 0f) continue;
                score += GetAllyValueScore(ally) + (1f - GetHealthRatio(ally)) * 100f;
                if (score > bestScore) { bestScore = score; best = ally; }
            }
            return best;
        }

        // dispel whoever has the most dangerous buffs
        static Hero GetBestDispelTarget()
        {
            Hero best = null; float bestScore = -1f;
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
            {
                if (foe.health <= 0) continue;
                float score = 0f;
                if (HasStatus(foe, StatusEffect.AutoLife)) score += 160f;
                if (HasStatus(foe, StatusEffect.Haste))    score += 140f;
                if (HasStatus(foe, StatusEffect.Brave))    score += 100f;
                if (HasStatus(foe, StatusEffect.Faith))    score += 100f;
                if (score <= 0f) continue;
                score += GetEnemyThreatScore(foe);
                if (score > bestScore) { bestScore = score; best = foe; }
            }
            return best;
        }

        // picks target based on threat + low hp + low defense
        static Hero GetBestPhysicalTarget()
        {
            Hero best = null; float bestScore = -999999f;
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
            {
                if (foe.health <= 0) continue;
                float score = GetEnemyThreatScore(foe)
                            + (1f - GetHealthRatio(foe)) * 150f
                            - foe.physicalDefense * 1.2f;
                if (HasStatus(foe, StatusEffect.Poison)) score += 50f;
                if (foe.jobClass == HeroJobClass.Alchemist) score += 500f;
                if (score > bestScore) { bestScore = score; best = foe; }
            }
            return best;
        }

        static Hero GetBestDeadAllyToRevive()
        {
            Hero best = null; float bestScore = -999999f;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health > 0) continue;
                float s = GetAllyValueScore(ally);
                if (s > bestScore) { bestScore = s; best = ally; }
            }
            return best;
        }

        static Hero GetLowestHealthAlly()
        {
            Hero lowest = null; float lowestRatio = 2f;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health <= 0) continue;
                float r = GetHealthRatio(ally);
                if (r < lowestRatio) { lowestRatio = r; lowest = ally; }
            }
            return lowest;
        }

        static Hero GetLowestManaAlly(float threshold)
        {
            Hero lowest = null; float lowestRatio = threshold + 0.001f;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health <= 0) continue;
                float r = GetManaRatio(ally);
                if (r <= lowestRatio) { lowestRatio = r; lowest = ally; }
            }
            return lowest;
        }

        // speed + attack + special weighted
        static float GetEnemyThreatScore(Hero hero)
        {
            if (hero == null) return -999999f;
            return hero.speed * 2.5f + hero.physicalAttack * 2.0f
                 + hero.special * 2.0f + hero.mana * 0.5f
                 + (1f - GetHealthRatio(hero)) * 40f;
        }

        static float GetAllyValueScore(Hero hero)
        {
            if (hero == null) return -999999f;
            return hero.speed * 2.0f + hero.physicalAttack * 1.8f
                 + hero.special * 1.8f + hero.mana * 0.5f;
        }

        static float GetHealthRatio(Hero hero)
        {
            if (hero == null || hero.maxHealth <= 0) return 2f;
            return (float)hero.health / hero.maxHealth;
        }

        static float GetManaRatio(Hero hero)
        {
            if (hero == null || hero.maxMana <= 0) return 2f;
            return (float)hero.mana / hero.maxMana;
        }

        static bool HasStatus(Hero hero, StatusEffect status)
        {
            if (hero == null) return false;
            foreach (StatusEffectAndDuration se in hero.statusEffectsAndDurations)
                if (se.statusEffect == status) return true;
            return false;
        }

       
        //checks if enemy used silence remedy stop wasting mana on silencestrike
        static bool BossCounterIsActive()
        {
            try
            {
                var report = TeamHeroCoder.BattleState.battleReport;
                if (report == null) return CounterIsActive;

                foreach (var entry in report)
                {
                    var type = entry.GetType();
                    var abilityField = type.GetField("ability");
                    var actorField   = type.GetField("heroWithInitiative");
                    if (abilityField == null || actorField == null) continue;

                    string abilityName = abilityField.GetValue(entry)?.ToString() ?? "";
                    object actor = actorField.GetValue(entry);
                    if (actor == null) continue;

                    bool actorIsFoe = false;
                    foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
                        if (ReferenceEquals(foe, actor)) { actorIsFoe = true; break; }

                    // if an enemy uses a silence remedy, stop wasting mana on silencestrike
                    if (actorIsFoe && (abilityName.Contains("SilenceRemedy") || abilityName.Contains("Silence Remedy")))
                        EnemyHasSilenceRemedy = true;

                    if (!actorIsFoe) continue;

                    bool isCounter = abilityName == "1001" || abilityName == "1002"
                                  || abilityName.Contains("Counter")
                                  || abilityName.Contains("Explode")
                                  || abilityName.Contains("Laser")
                                  || abilityName.Contains("Tail");

                    if (isCounter)
                    {
                        CounterIsActive = true;
                        Console.WriteLine($"  [Counter armed: {abilityName}]");
                    }
                    else if (abilityName == "Attack" || abilityName == "Wait")
                    {
                        if (CounterIsActive) Console.WriteLine("  [Counter cleared]");
                        CounterIsActive = false;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  [Counter check error: {ex.Message}]"); }

            if (CounterIsActive) Console.WriteLine("  *** Counter active — Wait! ***");
            return CounterIsActive;
        }

        static Hero GetFirstLivingFoe()
        {
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
                if (foe.health > 0) return foe;
            return null;
        }

        static bool TeamNeedsPotion()
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && GetHealthRatio(ally) <= 0.70f) return true;
            return false;
        }

        static bool InventoryHasPotion() => CountPotionsInInventory() > 0;

        static int CountPotionsInInventory()
        {
            int count = 0;
            foreach (var item in TeamHeroCoder.BattleState.allyInventory)
                if (item.ToString() == "Potion") count++;
            return count;
        }

        static int CountEthersInInventory() => EtherCount;

        static int CountEssenceInInventory() => TeamHeroCoder.BattleState.allyEssenceCount;

        static void SyncPotionCount()
        {
        
        }

        static void FallbackAttack(Hero actor)
        {
            if (CounterIsActive)
            {
                Hero safe = GetBestNonCounterTarget();
                if (safe != null)
                {
                    Console.WriteLine($"Fallback: {actor.jobClass} attacks {safe.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Attack, safe);
                    return;
                }
                Console.WriteLine($"Fallback: {actor.jobClass} waits — counter active");
                TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor);
                return;
            }
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
            {
                if (foe.health > 0)
                {
                    Console.WriteLine($"Fallback: {actor.jobClass} attacks {foe.jobClass}");
                    TeamHeroCoder.PerformHeroAbility(Ability.Attack, foe);
                    return;
                }
            }
            Console.WriteLine("No valid target found.");
        }
    }
}