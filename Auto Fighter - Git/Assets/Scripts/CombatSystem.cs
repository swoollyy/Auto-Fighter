using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Game.Combat
{
    public class CombatSystem
    {

        private BaseCharacter player1;
        private BaseCharacter player2;

        public List<BaseCharacter> upcomingTurns = new List<BaseCharacter>(8);

        protected BaseCharacter firstAttacker;

        private bool firstAttackerRemoved;

        public float multiAtkChancePercent;


        private float attackerAcc;
        private float attackerBrk;
        private float attackerCrt;

        private float defenderEva;
        private float defenderDef;
        private float defenderRes;

        private float AccEvaRatio;
        private float BrkDefRatio;
        private float CrtResRatio;

        private float critRatio;
        private float blockRatio;

        private float dodgePenalty;
        private float dodgeChance;
        private float hitChance;

        private float damage;

        private float dmgPen;



        bool doOnce;

        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void Initialize(BaseCharacter p1, BaseCharacter p2)
        {
            player1 = p1;
            player2 = p2;
        }

        public BaseCharacter DetermineFirstTurn()
        {
            if(player1.Speed.Value >= player2.Speed.Value)
            {
                firstAttacker = player1;
                player1.previousHits++;
                return player1;
            }
            else if(player2.Speed.Value > player1.Speed.Value)
            {
                firstAttacker = player2;
                player2.previousHits++;
                return player2;
            }

                return null;
        }

        public void DetermineTurns()
        {


            if(player1 != null && player2 != null)
            {

                const int maxTurns = 8;
                while (upcomingTurns.Count < maxTurns)
                {
                    player1.FillTurnMeter();
                        player2.FillTurnMeter();



                    if (player1.previousHits > 0)
                    {
                        if (LuckyHit(player1, player2, player1.previousHits))
                        {
                            player2.previousHits = 0;
                            player1.previousHits++;
                            upcomingTurns.Add(player1);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P1 Lucky Hit!");
                        }
                        else if (player1.Speed.Value > (player2.Speed.Value * 5))
                        {
                            if (player1.previousHits >= player1.Speed.Value / player2.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player2);
                                player1.previousHits = 0;
                            }
                        }
                        else
                        {
                            player1.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }
                    else if (player2.previousHits > 0)
                    {
                        if (LuckyHit(player2, player1, player2.previousHits))
                        {
                            player1.previousHits = 0;
                            player2.previousHits++;
                            upcomingTurns.Add(player2);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P2 Lucky Hit!");
                        }
                        else if (player2.Speed.Value > (player1.Speed.Value * 5))
                        {
                            if (player2.previousHits >= player2.Speed.Value / player1.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player1);
                                player2.previousHits = 0;
                            }
                        }
                        else
                        {
                            player2.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }

                    if (player1.IsTurnReady && (!player2.IsTurnReady || player1.TurnMeter >= player2.TurnMeter))
                    {
                        player1.previousHits++;
                        upcomingTurns.Add(player1);
                        Debug.Log("P1 Added from turn!");
                        player2.previousHits = 0;
                        player1.ConsumeTurnMeter();
                    }
                    else if (player2.IsTurnReady)
                    {
                        player2.previousHits++;
                        upcomingTurns.Add(player2);
                        Debug.Log("P2 Added from turn!");
                        player1.previousHits = 0;
                        player2.ConsumeTurnMeter();
                    }
                }
                if (upcomingTurns[0] != null)
                    if (upcomingTurns[0].name == firstAttacker.name && !firstAttackerRemoved)
                    {
                        upcomingTurns.RemoveAt(0);
                        firstAttackerRemoved = true;
                    }



            }

        }


        public void ExecuteAttack(BaseCharacter attacker, BaseCharacter defender)
        {
            damage = Random.Range(attacker.MinAtk.Value, attacker.MaxAtk.Value);

            attackerAcc = attacker.Accuracy.Value;
            attackerBrk = attacker.Break.Value;
            attackerCrt = attacker.Critical.Value;

            defenderEva = defender.Evasion.Value;
            defenderDef = defender.Defense.Value;
            defenderRes = defender.Resistance.Value;



            //Evasion-to-Accuracy check //Dodge


            //Break-to-Defense check //Penetration or Defense


            





            if (WillAttackerHit())
              {
                Debug.Log($" {attacker.name} Can hit! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
                if(!WillDefenderDodge())
                {
                    CalculateDamage();
                    Debug.Log($"{attacker.name} Dmg Pen - {dmgPen}\nDamage - {damage}");
                        if (WillAttackerCrit())
                        {
                            damage *= 1.5f;
                            Debug.Log($" {attacker.name} Crit hit! \nCrit % - {critRatio}");
                        }
                        else if(WillDefenderBlock())
                        {
                            damage *= .5f;
                            Debug.Log($" {defender.name} Blocked hit! \nBlock % - {blockRatio}");
                        }
                    defender.Health.baseValue -= damage;
                }
                else
                    Debug.Log($" {defender.name} Dodged! \nHit % - {hitChance} \nDodge % - {dodgeChance}");

            }
              else
                Debug.Log($" {attacker.name} Missed! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
            }
        public bool LuckyHit(BaseCharacter attacker, BaseCharacter defender, int priorHits)
        {
            float decayRate = 0.2f;
            if (priorHits > 5f)
                decayRate = .3f;
            else if (priorHits > 10f)
                decayRate += .5f;
            float penaltyMultiplier = Mathf.Exp(-decayRate * priorHits); // Shrinks toward 0 over time
            float speedValueAtkr = Mathf.Max(1, attacker.Speed.Value * penaltyMultiplier);
            float speedValueDfdr = defender.Speed.Value;

            float speedRatio = Mathf.Log((speedValueAtkr / speedValueDfdr) + 1, 2f);


            float rawSpeedDiff = Mathf.Abs(speedValueAtkr - speedValueDfdr);

            float scaleFactor = Mathf.Lerp(5f, 2f, Mathf.Clamp01(rawSpeedDiff / 9999f));

            float chance = Mathf.Clamp01(speedRatio / scaleFactor);

            float luck = Mathf.Max(0f, attacker.Luck.Value);
            float luckBonus = Mathf.Clamp01(luck / 99999f) * .25f;
            
            multiAtkChancePercent = chance;

            float finalChance = Mathf.Clamp01(chance + luckBonus);

            Debug.Log($"Luck Bonus -  {attacker.name} {luckBonus}");
            Debug.Log($"Chance -  {attacker.name} {chance}");
            Debug.Log($"Combo -  {attacker.name} {luckBonus + chance}");

            return Random.value < chance;
        }

        public bool WillAttackerHit()
        {
            //Accuracy-to-Evasion check //Accuracy
            AccEvaRatio = attackerAcc / Mathf.Max(1f, defenderEva);
            hitChance = (float)System.Math.Tanh((double)(AccEvaRatio / 1.88f));
            return (Random.value < hitChance);
        }

        public bool WillDefenderDodge()
        {
            dodgePenalty = Mathf.Lerp(0f, 0.15f, 1f - hitChance);
            dodgeChance = Mathf.Clamp01(dodgeChance - dodgePenalty);
            if (defenderEva >= attackerAcc)
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = AccEvaRatio / (AccEvaRatio + 2.5f);
            }
            else
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = 0.5f * (AccEvaRatio / (AccEvaRatio + 1.5f));
            }
            dodgeChance = Mathf.Clamp01(dodgeChance);
            return (Random.value < Mathf.Clamp01(dodgeChance - dodgePenalty));
        }

        public bool WillAttackerCrit()
        {
            //Crit-to-Resistance check //Crits or Block

            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            critRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                critRatio = 0f;
            }
            else if (CrtResRatio > 1f)
            {
                critRatio = Mathf.Log10(CrtResRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }

            Debug.Log($"Crit % - {critRatio}");


            if (attackerCrt == 0f)
                return false;
            else if (attackerCrt > defenderRes)
                return (Random.value < critRatio);
            else
            {
                Debug.Log($"Buffed Crit %! - {critRatio+ .05f}");
                return (Random.value < critRatio + .05f);
            }
        }

        public bool WillDefenderBlock()
        {
            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            blockRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                blockRatio = 0f;
            }
            else if(CrtResRatio <= 1f)
            {
                float inverseRatio = defenderRes / Mathf.Max(1f, attackerCrt);
                blockRatio = Mathf.Clamp01(Mathf.Log10(inverseRatio) / Mathf.Log10(10f)); // down to -1.0 (-100%)
            }

            Debug.Log($"Block % - {blockRatio}");

            if (defenderRes == 0f)
                return false;
            else if (defenderRes >= attackerCrt)
                return (Random.value < blockRatio);
            else
            {
                Debug.Log($"Buffed Block %! - {blockRatio + .05f}");
                return (Random.value < blockRatio + .05f);
            }
        }

        public void CalculateDamage()
        {
            BrkDefRatio = attackerBrk / Mathf.Max(1f, defenderDef);

            if (Mathf.Approximately(BrkDefRatio, 1f))
            {
                dmgPen = 0f;
            }
            else if (BrkDefRatio > 1f)
            {
                dmgPen = Mathf.Log10(BrkDefRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }
            else
            {
                float inverseRatio = defenderDef / Mathf.Max(1f, attackerBrk);
                dmgPen = -Mathf.Log10(inverseRatio) / Mathf.Log10(10f); // down to -1.0 (-100%)
            }

            damage = Mathf.Round((damage + (damage * dmgPen)) * 10f) / 10f;
        }

    }



}

