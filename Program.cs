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
    // vs Rogue (Assassin+Bandit) / 2 Wizards (Aberrant) / Fighter (Guardian+Paladin)
    // Double Petrify is the main threat cleanse it 
    // Rogue Stealth bypasses Fighter 
    // Silence both Wizards
    // Kill order: their Rogue > Wizards > Fighter
    // Alchemist crafts Petrify Remedies from Essence

    public static class MyAI
    {
        public static string FolderExchangePath = "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // starting items: 1 potion, 1 revive, 4 petrify remedies, 3 full remedies, 20 essence
        public static int PotionCount = 1;
        public static int EtherCount  = 0;

        // thresholds
        const float LOW_HP_CRITICAL = 0.30f;
        const float LOW_HP_SERIOUS  = 0.55f;
        const float LOW_HP_LIGHT    = 0.70f;
        const float LOW_MANA        = 0.30f;

        static readonly HeroJobClass[] SilencePriority = {
            HeroJobClass.Rogue,   HeroJobClass.Wizard,  HeroJobClass.Cleric,
            HeroJobClass.Alchemist, HeroJobClass.Fighter, HeroJobClass.Monk,
        };
        static readonly HeroJobClass[] SlowPriority = {
            HeroJobClass.Wizard,  HeroJobClass.Rogue,   HeroJobClass.Monk,
            HeroJobClass.Fighter, HeroJobClass.Cleric,  HeroJobClass.Alchemist,
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

            if (actor.jobClass == HeroJobClass.Rogue)
                HandleRogue(actor);
            else if (actor.jobClass == HeroJobClass.Cleric)
                HandleCleric(actor);
            else if (actor.jobClass == HeroJobClass.Alchemist)
                HandleAlchemist(actor);
            else
                Act(actor, Ability.Wait, actor);
        }

        // ROGUE
        // cleanse petrify immediately > use items for Item Jockey > silence Wizards > attack their Rogue
        static void HandleRogue(Hero actor)
        {
            Console.WriteLine("Rogue branch");

            // petrify remedies trigger Item Jockey too
            if (Utility.AreAbilityAndTargetLegal(Ability.PetrifyRemedy, actor, false))
            {
                foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                {
                    if (ally.health <= 0) continue;
                    if (HasStatus(ally, StatusEffect.Petrified) || HasStatus(ally, StatusEffect.Petrifying))
                    { Act(actor, Ability.PetrifyRemedy, ally); return; }
                }
            }

            // use full remedy on silenced or poisoned allies
            if (Utility.AreAbilityAndTargetLegal(Ability.FullRemedy, actor, false))
            {
                foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                {
                    if (ally.health <= 0) continue;
                    if (HasStatus(ally, StatusEffect.Doom) || HasStatus(ally, StatusEffect.Silence)
                        || HasStatus(ally, StatusEffect.Poison))
                    { Act(actor, Ability.FullRemedy, ally); return; }
                }
            }

            // use ether on whoever needs mana triggers Item Jockey
            if (EtherCount > 0)
            {
                Hero etherTarget = GetLowestManaAlly(LOW_MANA);
                if (etherTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Ether, etherTarget, false))
                    if (TryUseItem(actor, Ability.Ether, etherTarget, ref EtherCount)) return;
            }

            // use potion on whoever needs HP triggers Item Jockey
            if (PotionCount > 0)
            {
                Hero potionTarget = GetLowestHealthAlly();
                if (potionTarget != null && GetHealthRatio(potionTarget) <= LOW_HP_LIGHT)
                    if (TryUseItem(actor, Ability.Potion, potionTarget, ref PotionCount)) return;
            }

            // silence Wizards first 
            if (actor.mana >= 15)
            {
                Hero target = GetPriorityFoeTarget(SilencePriority, StatusEffect.Silence, reapplyBelowDuration: 1);
                if (target != null && Utility.AreAbilityAndTargetLegal(Ability.SilenceStrike, target, false))
                { Act(actor, Ability.SilenceStrike, target); return; }
            }

            // attack their Rogue first — most dangerous enemy
            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // CLERIC
        // res > cleanse petrify > cleanse other statuses > heal > autolife > faith > haste > attack
        static void HandleCleric(Hero actor)
        {
            Console.WriteLine("Cleric branch");

            if (actor.mana >= 25)
            {
                Hero dead = GetBestDeadAllyToRevive();
                if (dead != null && Utility.AreAbilityAndTargetLegal(Ability.Resurrection, dead, false))
                { Act(actor, Ability.Resurrection, dead); return; }
            }

            // quickcleanse petrify first — highest priority
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health <= 0) continue;
                if ((HasStatus(ally, StatusEffect.Petrified) || HasStatus(ally, StatusEffect.Petrifying))
                    && actor.mana >= 10
                    && Utility.AreAbilityAndTargetLegal(Ability.QuickCleanse, ally, false))
                { Act(actor, Ability.QuickCleanse, ally); return; }
            }

            Hero cleanseTarget = GetBestCleanseTarget();
            if (cleanseTarget != null && actor.mana >= 10
                && Utility.AreAbilityAndTargetLegal(Ability.QuickCleanse, cleanseTarget, false))
            { Act(actor, Ability.QuickCleanse, cleanseTarget); return; }

            Hero lowestAlly = GetLowestHealthAlly();
            if (lowestAlly != null)
            {
                float ratio = GetHealthRatio(lowestAlly);

                if (ratio <= LOW_HP_CRITICAL && actor.mana >= 15
                    && Utility.AreAbilityAndTargetLegal(Ability.QuickHeal, lowestAlly, false))
                    { Act(actor, Ability.QuickHeal, lowestAlly); return; }

                if (ratio <= LOW_HP_CRITICAL && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;

                if (ratio <= LOW_HP_SERIOUS && actor.mana >= 20
                    && Utility.AreAbilityAndTargetLegal(Ability.CureSerious, lowestAlly, false))
                    { Act(actor, Ability.CureSerious, lowestAlly); return; }

                if (ratio <= LOW_HP_SERIOUS && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;

                if (ratio <= LOW_HP_LIGHT && actor.mana >= 10
                    && Utility.AreAbilityAndTargetLegal(Ability.CureLight, lowestAlly, false))
                    { Act(actor, Ability.CureLight, lowestAlly); return; }

                if (ratio <= LOW_HP_LIGHT && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;
            }

            if (actor.mana >= 25)
            {
                Hero autoLifeTarget = GetBestAutoLifeTarget();
                if (autoLifeTarget != null && Utility.AreAbilityAndTargetLegal(Ability.AutoLife, autoLifeTarget, false))
                { Act(actor, Ability.AutoLife, autoLifeTarget); return; }
            }

            if (actor.mana >= 15)
            {
                Hero faithTarget = GetPriorityAllyTarget(FaithPriority, StatusEffect.Faith, reapplyBelowDuration: 1);
                if (faithTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Faith, faithTarget, false))
                { Act(actor, Ability.Faith, faithTarget); return; }
            }

            if (actor.mana >= 15)
            {
                Hero hasteTarget = GetPriorityAllyTarget(HastePriority, StatusEffect.Haste, reapplyBelowDuration: 1);
                if (hasteTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Haste, hasteTarget, false))
                { Act(actor, Ability.Haste, hasteTarget); return; }
            }

            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // ALCHEMIST
        // craft ether > use ether > craft petrify remedy > slow wizards > attack
        static void HandleAlchemist(Hero actor)
        {
            Console.WriteLine("Alchemist branch");

            // craft ether first — nothing works without mana
            if (EtherCount == 0 && CountEssenceInInventory() >= 2)
            {
                Console.WriteLine($"Alchemist crafts Ether (Essence: {CountEssenceInInventory()})");
                TeamHeroCoder.PerformHeroAbility(Ability.CraftEther, actor);
                EtherCount++;
                return;
            }

            // use ether on whoever needs mana
            if (EtherCount > 0)
            {
                Hero etherTarget = GetLowestManaAlly(0.50f);
                if (etherTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Ether, etherTarget, false))
                { TryUseItem(actor, Ability.Ether, etherTarget, ref EtherCount); return; }
            }

            // craft petrify remedy if running low
            if (!AllyInventoryHasPetrifyRemedy() && CountEssenceInInventory() >= 2)
            {
                Console.WriteLine($"Alchemist crafts Petrify Remedy (Essence: {CountEssenceInInventory()})");
                TeamHeroCoder.PerformHeroAbility(Ability.CraftPetrifyRemedy, actor);
                return;
            }

            // slow both Wizards
            if (actor.mana >= 15)
            {
                Hero slowTarget = GetPriorityFoeTarget(SlowPriority, StatusEffect.Slow, reapplyBelowDuration: 1);
                if (slowTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Slow, slowTarget, false))
                { Act(actor, Ability.Slow, slowTarget); return; }
            }

            if (actor.mana >= 15)
            {
                foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
                {
                    if (foe.health <= 0 || foe.jobClass != HeroJobClass.Fighter) continue;
                    if (HasStatus(foe, StatusEffect.AutoLife)
                        && Utility.AreAbilityAndTargetLegal(Ability.Dispel, foe, false))
                    { Act(actor, Ability.Dispel, foe); return; }
                }
            }

            // cleanse any petrified allies
            if (actor.mana >= 15)
            {
                Hero cleanseTarget = GetBestCleanseTarget();
                if (cleanseTarget != null && Utility.AreAbilityAndTargetLegal(Ability.Cleanse, cleanseTarget, false))
                { Act(actor, Ability.Cleanse, cleanseTarget); return; }
            }

            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        static void Act(Hero actor, Ability ability, Hero target)
        {
            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
        }

        static bool TryUseItem(Hero actor, Ability ability, Hero target, ref int itemCount)
        {
            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
            itemCount = Math.Max(0, itemCount - 1);
            return true;
        }

        // ============================================================
        // TARGET SELECTORS
        // ============================================================

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

        // their Rogue steals our remedies with Larceny kill it first
        static Hero GetBestLegalAttackTarget()
        {
            Hero best = null; float bestScore = -999999f;
            foreach (Hero foe in TeamHeroCoder.BattleState.foeHeroes)
            {
                if (foe.health <= 0) continue;
                if (!Utility.AreAbilityAndTargetLegal(Ability.Attack, foe, false)) continue;

                float score = GetEnemyThreatScore(foe)
                            + (1f - GetHealthRatio(foe)) * 150f
                            - foe.physicalDefense * 1.2f;
                if (HasStatus(foe, StatusEffect.Poison)) score += 50f;
                if (foe.jobClass == HeroJobClass.Rogue)  score += 800f;
                if (score > bestScore) { bestScore = score; best = foe; }
            }
            return best;
        }

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
                if (HasStatus(ally, StatusEffect.Petrified))  score += 250f;
                if (HasStatus(ally, StatusEffect.Petrifying)) score += 200f;
                if (HasStatus(ally, StatusEffect.Doom))       score += 180f;
                if (HasStatus(ally, StatusEffect.Silence))    score += 120f;
                if (HasStatus(ally, StatusEffect.Poison))     score += 80f;
                if (score <= 0f) continue;
                score += GetAllyValueScore(ally) + (1f - GetHealthRatio(ally)) * 100f;
                if (score > bestScore) { bestScore = score; best = ally; }
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

        // ============================================================
        // SCORING + STATUS HELPERS
        // ============================================================

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

        static int GetStatusDuration(Hero hero, StatusEffect status)
        {
            if (hero == null) return 0;
            foreach (StatusEffectAndDuration se in hero.statusEffectsAndDurations)
                if (se.statusEffect == status) return se.duration;
            return 0;
        }

        static bool HasStatus(Hero hero, StatusEffect status)
        {
            if (hero == null) return false;
            foreach (StatusEffectAndDuration se in hero.statusEffectsAndDurations)
                if (se.statusEffect == status) return true;
            return false;
        }

        static bool AllyInventoryHasPetrifyRemedy()
        {
            foreach (InventoryItem ii in TeamHeroCoder.BattleState.allyInventory)
                if (ii.item == Item.PetrifyRemedy && ii.count > 0) return true;
            return false;
        }

        static bool TeamNeedsPotion()
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && GetHealthRatio(ally) <= LOW_HP_LIGHT) return true;
            return false;
        }

        static int CountEssenceInInventory() => TeamHeroCoder.BattleState.allyEssenceCount;
    }
}