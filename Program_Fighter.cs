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

    // My team: Rogue / Fighter / Cleric
    // Fighter uses Cover passive to protect the Cleric from rushdown
    // Rogue runs Item Jockey engine for extra turns
    // Cleric heals the Fighter who is tanking all the hits

    public static class MyAI
    {
        public static string FolderExchangePath = "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // starting items: 4 potions and 2 ethers 
        public static int PotionCount = 4;
        public static int EtherCount  = 2;

        // thresholds
        const float LOW_HP_CRITICAL = 0.30f;
        const float LOW_HP_SERIOUS  = 0.55f;
        const float LOW_HP_LIGHT    = 0.70f;
        const float LOW_MANA        = 0.30f;

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
            else if (actor.jobClass == HeroJobClass.Fighter)
                HandleFighter(actor);
            else
                Act(actor, Ability.Wait, actor);
        }

        // ROGUE
        // vs fighter rushdown — use items for Item Jockey > attack
        static void HandleRogue(Hero actor)
        {
            Console.WriteLine("Rogue branch");

            // use ether on whoever needs mana — triggers Item Jockey
            if (EtherCount > 0)
            {
                Hero etherTarget = GetLowestManaAlly(0.50f);
                if (etherTarget != null)
                    if (TryUseItem(actor, Ability.Ether, etherTarget, ref EtherCount)) return;
            }

            // use potion on whoever needs HP — triggers Item Jockey
            if (PotionCount > 0)
            {
                Hero potionTarget = GetLowestHealthAlly();
                if (potionTarget != null && GetHealthRatio(potionTarget) <= 0.85f)
                    if (TryUseItem(actor, Ability.Potion, potionTarget, ref PotionCount)) return;
            }

            // skip silence and poison — fighters don't use mana and they have 6 poison remedies
            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // CLERIC
        // vs fighter rushdown — heal aggressively, fighters hit hard and fast
        // res > cleanse > heal > autolife > faith > haste > attack
        static void HandleCleric(Hero actor)
        {
            Console.WriteLine("Cleric branch");

            if (actor.mana >= 25)
            {
                Hero dead = GetBestDeadAllyToRevive();
                if (dead != null) { Act(actor, Ability.Resurrection, dead); return; }
            }

            Hero cleanseTarget = GetBestCleanseTarget();
            if (cleanseTarget != null) { Act(actor, Ability.QuickCleanse, cleanseTarget); return; }

            Hero lowestAlly = GetLowestHealthAlly();
            if (lowestAlly != null)
            {
                float ratio = GetHealthRatio(lowestAlly);

                // against rushdown treat serious HP as critical — don't wait
                if (ratio <= LOW_HP_SERIOUS && actor.mana >= 15)
                    { Act(actor, Ability.QuickHeal, lowestAlly); return; }

                if (ratio <= LOW_HP_CRITICAL && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;

                if (ratio <= LOW_HP_SERIOUS && actor.mana >= 20)
                    { Act(actor, Ability.CureSerious, lowestAlly); return; }

                if (ratio <= LOW_HP_SERIOUS && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;

                if (ratio <= LOW_HP_LIGHT && actor.mana >= 10)
                    { Act(actor, Ability.CureLight, lowestAlly); return; }

                if (ratio <= LOW_HP_LIGHT && PotionCount > 0)
                    if (TryUseItem(actor, Ability.Potion, lowestAlly, ref PotionCount)) return;
            }

            // rogue gets autolife first since it dies the most
            if (actor.mana >= 25)
            {
                Hero autoLifeTarget = GetBestAutoLifeTarget();
                if (autoLifeTarget != null) { Act(actor, Ability.AutoLife, autoLifeTarget); return; }
            }

            if (actor.mana >= 15)
            {
                Hero faithTarget = GetPriorityAllyTarget(FaithPriority, StatusEffect.Faith, reapplyBelowDuration: 1);
                if (faithTarget != null) { Act(actor, Ability.Faith, faithTarget); return; }
            }

            if (actor.mana >= 15)
            {
                Hero hasteTarget = GetPriorityAllyTarget(HastePriority, StatusEffect.Haste, reapplyBelowDuration: 1);
                if (hasteTarget != null) { Act(actor, Ability.Haste, hasteTarget); return; }
            }

            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // FIGHTER
        // Cover passive automatically intercepts hits aimed at the Cleric
        // priority: res if Cleric dead > use potion on Cleric > Brave on self > QuickHit > attack
        static void HandleFighter(Hero actor)
        {
            Console.WriteLine("Fighter branch");

            if (actor.mana >= 25)
            {
                Hero dead = GetBestDeadAllyToRevive();
                if (dead != null) { Act(actor, Ability.Resurrection, dead); return; }
            }

            // use potion on Cleric to keep our healer alive
            if (PotionCount > 0)
            {
                Hero potionTarget = GetLowestHealthAlly();
                if (potionTarget != null && GetHealthRatio(potionTarget) <= LOW_HP_SERIOUS)
                    if (TryUseItem(actor, Ability.Potion, potionTarget, ref PotionCount)) return;
            }

            // stack Brave on self — Cover means we're taking hits so we should hit back hard
            if (actor.mana >= 15 && !HasStatus(actor, StatusEffect.Brave))
            { Act(actor, Ability.Brave, actor); return; }

            // QuickHit for extra tempo when mana allows
            if (actor.mana >= 15)
            {
                Hero target = GetBestLegalAttackTarget();
                if (target != null) { Act(actor, Ability.QuickHit, target); return; }
            }

            Hero attackTarget = GetBestLegalAttackTarget();
            if (attackTarget != null) { Act(actor, Ability.Attack, attackTarget); return; }

            Act(actor, Ability.Wait, actor);
        }

        // ============================================================
        // HELPERS — perform ability 
        // ============================================================

        // performs an ability and logs it — keeps handlers clean
        static void Act(Hero actor, Ability ability, Hero target)
        {
            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
        }

        // uses a consumable item and decreases count
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

        // walks priority list, returns first foe whose status duration is at or below threshold
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

        // uses AreAbilityAndTargetLegal to skip illegal targets 
        // cleric gets +500 priority since killing their healer first is key
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
                if (HasStatus(foe, StatusEffect.Poison))     score += 50f;
                if (foe.jobClass == HeroJobClass.Cleric)     score += 500f;
                if (score > bestScore) { bestScore = score; best = foe; }
            }
            return best;
        }

        // cleric gets autolife on itself first against rushdown 
        // then rogue, then whoever scores highest
        static Hero GetBestAutoLifeTarget()
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && ally.jobClass == HeroJobClass.Cleric
                    && !HasStatus(ally, StatusEffect.AutoLife))
                    return ally;

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

        // speed + attack + special
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

        static bool TeamNeedsPotion()
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
                if (ally.health > 0 && GetHealthRatio(ally) <= LOW_HP_LIGHT) return true;
            return false;
        }

    }
}