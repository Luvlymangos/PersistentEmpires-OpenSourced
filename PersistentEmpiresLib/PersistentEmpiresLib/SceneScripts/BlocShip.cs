﻿using PersistentEmpiresLib.Helpers;
using PersistentEmpiresLib.PersistentEmpiresMission.MissionBehaviors;
using PersistentEmpiresLib.SceneScripts.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace PersistentEmpiresLib.SceneScripts
{

    public class PE_BlocShip : PE_MoveableMachine
    {
        // Credits to fucking Bloc
        public bool isPlayerUsing = false;
        public string Animation = "";
        public int StrayDurationSeconds = 7200;

        public string RidingSkillId = "";
        public int RidingSkillRequired = 0;

        public string RepairingSkillId = "";
        public int RepairingSkillRequired = 0;

        public string RepairItemRecipies = "pe_hardwood*2,pe_wooden_stick*1";
        public int RepairDamage = 20;
        public string RepairItem = "pe_buildhammer";
        public string ParticleEffectOnDestroy = "";
        public string SoundEffectOnDestroy = "";
        public string ParticleEffectOnRepair = "";
        public string SoundEffectOnRepair = "";
        public bool DestroyedByStoneOnly = false;

        private long WillBeDeletedAt = 0;
        private SkillObject RidingSkill;
        private SkillObject RepairSkill;

        private List<RepairReceipt> receipt = new List<RepairReceipt>();
        private bool _landed;
        private bool destroyed = false;
        public override ScriptComponentBehavior.TickRequirement GetTickRequirement() => !this.GameEntity.IsVisibleIncludeParents() ? base.GetTickRequirement() : ScriptComponentBehavior.TickRequirement.Tick | ScriptComponentBehavior.TickRequirement.TickParallel;

        private void ParseRepairReceipts()
        {
            string[] repairReceipt = this.RepairItemRecipies.Split(',');
            foreach (string receipt in repairReceipt)
            {
                string[] inflictedReceipt = receipt.Split('*');
                string receiptId = inflictedReceipt[0];
                int count = int.Parse(inflictedReceipt[1]);
                this.receipt.Add(new RepairReceipt(receiptId, count));
            }
        }

        private void CheckIfLanded(MatrixFrame oldFrame)
        {
            if (base.GameEntity?.GlobalPosition == null || oldFrame == null)
                return;

            Vec3 startPosition = base.GameEntity.GlobalPosition;
            float frontRearRadius = 0.1f; // Adjust the front and rear radius as needed
            float sideRadius = 0.1f; // Adjust the side radius as needed

            // Set a Default Value for Rays TODO

            // Define the ray directions for front, rear, left, and right
            Vec3[] rayDirections = new Vec3[]
            {
                base.GameEntity.GetGlobalFrame().rotation.f,  // Front
                -base.GameEntity.GetGlobalFrame().rotation.f, // Rear
                -base.GameEntity.GetGlobalFrame().rotation.s, // Left
                base.GameEntity.GetGlobalFrame().rotation.s   // Right
            };

            // Define the corresponding radius for each direction
            float[] rayRadii = new float[]
            {
                frontRearRadius, // Front
                frontRearRadius, // Rear
                sideRadius,      // Left
                sideRadius       // Right
            };

            for (int i = 0; i < rayDirections.Length; i++)
            {
                Vec3 direction = rayDirections[i];
                float radius = rayRadii[i];

                Vec3 raycastPosition = startPosition;
                Vec3 raycastEndPosition = raycastPosition + direction * radius;

                if (Mission.Current.Scene.RayCastForClosestEntityOrTerrain(raycastPosition, raycastEndPosition, out float hitDistance, out GameEntity hitEntity))
                {
                    if (hitEntity != base.GameEntity)
                    {
                        StopShip();
                        base.GameEntity.SetFrame(ref oldFrame);
                        Mission.Current.MakeSound(SoundEvent.GetEventIdFromString("event:/mission/siege/merlon/wood_destroy"), startPosition, false, true, -1, -1);
                        this.SetHitPoint(this.HitPoint - 10, Vec3.Zero);
                        break;
                    }
                    else if (hitDistance <= radius)
                    {
                        StopShip();
                        base.GameEntity.SetFrame(ref oldFrame);
                        Mission.Current.MakeSound(SoundEvent.GetEventIdFromString("event:/mission/siege/merlon/wood_destroy"), startPosition, false, true, -1, -1);
                        this.SetHitPoint(this.HitPoint - 10, Vec3.Zero);
                        break;
                    }
                }
            }
        }

        protected override void OnTick(float dt)
        {
            if (base.GameEntity == null) return;

            MatrixFrame oldFrame = base.GameEntity.GetFrame();
            base.OnTick(dt);

            if (GameNetwork.IsServer)
            {
                if (this.PilotAgent != null)
                {
                    if (this.RidingSkill != null && this.PilotAgent.Character.GetSkillValue(this.RidingSkill) < this.RidingSkillRequired)
                    {
                        this.PilotAgent.StopUsingGameObjectMT(false);
                        return;
                    }
                    this.ResetStrayDuration();
                }
            }

            if (GameNetwork.IsClient && Agent.Main != null && this.PilotAgent == Agent.Main)
            {
                HandleInput();
            }

            if (this.PilotAgent == null)
            {
                StopShip();
            }

            if (GameNetwork.IsServer && (IsShipMoving() || this.PilotAgent != null))
            {
                this.CheckIfLanded(oldFrame);
            }

            if (destroyed)
            {
                base.GameEntity.Remove(0);
                destroyed = false;
            }
        }

        private void HandleInput()
        {
            HandleMovement(InputKey.W, this.RequestMovingForward, this.RequestStopMovingForward);
            HandleMovement(InputKey.S, this.RequestMovingBackward, this.RequestStopMovingBackward);
            HandleMovement(InputKey.A, this.RequestTurningLeft, this.RequestStopTurningLeft);
            HandleMovement(InputKey.D, this.RequestTurningRight, this.RequestStopTurningRight);
            HandleMovement(InputKey.Space, this.RequestMovingUp, this.RequestStopMovingUp);
            HandleMovement(InputKey.LeftShift, this.RequestMovingDown, this.RequestStopMovingDown);

            if (Mission.Current.InputManager.IsKeyPressed(InputKey.F))
            {
                GameNetwork.MyPeer.ControlledAgent.HandleStopUsingAction();
                this.isPlayerUsing = false;
                ActionIndexCache ac = ActionIndexCache.act_none;
                this.PilotAgent.SetActionChannel(0, ac, true, 0UL, 0.0f, 1f, -0.2f, 0.4f, 0, false, -0.2f, 0, true);
            }
        }

        private void HandleMovement(InputKey key, Action startAction, Action stopAction)
        {
            if (Mission.Current.InputManager.IsKeyPressed(key))
            {
                startAction();
            }
            else if (Mission.Current.InputManager.IsKeyReleased(key))
            {
                stopAction();
            }
        }

        private bool IsShipMoving()
        {
            return base.IsMovingBackward || base.IsMovingDown || base.IsMovingForward || base.IsMovingUp || base.IsTurningLeft || base.IsTurningRight;
        }

        private void StopShip()
        {
            if (base.IsMovingBackward) this.StopMovingBackward();
            if (base.IsMovingDown) this.StopMovingDown();
            if (base.IsMovingForward) this.StopMovingForward();
            if (base.IsMovingUp) this.StopMovingUp();
            if (base.IsTurningLeft) this.StopTurningLeft();
            if (base.IsTurningRight) this.StopTurningRight();
        }


        protected override void OnTickParallel(float dt)
        {
            base.OnTickParallel(dt);
            if (!base.GameEntity.IsVisibleIncludeParents())
            {
                return;
            }
        }
        protected override void OnInit()
        {
            base.OnInit();
            foreach (StandingPoint standingPoint in this.StandingPoints)
            {
                standingPoint.AutoSheathWeapons = true;
            }
            if (this.RidingSkillId != "")
            {
                this.RidingSkill = MBObjectManager.Instance.GetObject<SkillObject>(this.RidingSkillId);
            }
            if (this.RepairingSkillId != "")
            {
                this.RepairSkill = MBObjectManager.Instance.GetObject<SkillObject>(this.RepairingSkillId);
            }
            this.ParseRepairReceipts();
            this.ResetStrayDuration();
            this.HitPoint = this.MaxHitPoint;
        }
        public bool IsAgentFullyUsing(Agent usingAgent)
        {
            return this.PilotAgent == usingAgent;
        }
        public override TextObject GetActionTextForStandingPoint(UsableMissionObject usableGameObject)
        {
            TextObject forStandingPoint = new TextObject(this.IsAgentFullyUsing(GameNetwork.MyPeer.ControlledAgent) ? "{=QGdaakYW}{KEY} Stop Using" : "{=bl2aRW8f}{KEY} Command Ship");
            forStandingPoint.SetTextVariable("KEY", HyperlinkTexts.GetKeyHyperlinkText(HotKeyManager.GetHotKeyId("CombatHotKeyCategory", 13)));
            return forStandingPoint;
        }

        public override string GetDescriptionText(GameEntity gameEntity = null)
        {
            return new TextObject("{=}Bloc's Ship").ToString();
        }

        public override bool IsStray()
        {
            if (this.PilotAgent != null) return false;
            return this.WillBeDeletedAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public override void ResetStrayDuration()
        {
            this.WillBeDeletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + this.StrayDurationSeconds;
        }

        public override void SetHitPoint(float hitPoint, Vec3 impactDirection)
        {
            this.HitPoint = hitPoint;
            MatrixFrame globalFrame = base.GameEntity.GetGlobalFrame();
            if (this.HitPoint > this.MaxHitPoint) this.HitPoint = this.MaxHitPoint;
            if (this.HitPoint < 0) this.HitPoint = 0;

            if (this.HitPoint == 0)
            {
                if (this.PilotAgent != null)
                {
                    this.PilotAgent.StopUsingGameObjectMT(false);
                }
                if (this.ParticleEffectOnDestroy != "")
                {
                    Mission.Current.Scene.CreateBurstParticle(ParticleSystemManager.GetRuntimeIdByName(this.ParticleEffectOnDestroy), globalFrame);
                }
                if (this.SoundEffectOnDestroy != "")
                {
                    Mission.Current.MakeSound(SoundEvent.GetEventIdFromString(this.SoundEffectOnDestroy), globalFrame.origin, false, true, -1, -1);
                }
                destroyed = true;
                // base.GameEntity.Remove(0);
            }
            if (this.HitPoint == this.MaxHitPoint)
            {
                if (this.ParticleEffectOnRepair != "")
                {
                    Mission.Current.Scene.CreateBurstParticle(ParticleSystemManager.GetRuntimeIdByName(this.ParticleEffectOnRepair), globalFrame);
                }
                if (this.SoundEffectOnRepair != "")
                {
                    Mission.Current.MakeSound(SoundEvent.GetEventIdFromString(this.SoundEffectOnRepair), globalFrame.origin, false, true, -1, -1);
                }
            }
        }


        protected override bool OnHit(Agent attackerAgent, int damage, Vec3 impactPosition, Vec3 impactDirection, in MissionWeapon weapon, ScriptComponentBehavior attackerScriptComponentBehavior, out bool reportDamage)
        {
            reportDamage = true;
            WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;

            if (attackerAgent != null && this.RepairSkill != null && attackerAgent.Character.GetSkillValue(this.RepairSkill) >= this.RepairingSkillRequired &&
                weapon.Item?.StringId == this.RepairItem && attackerAgent.IsHuman && attackerAgent.IsPlayerControlled && this.HitPoint != this.MaxHitPoint)
            {
                reportDamage = false;
                NetworkCommunicator player = attackerAgent.MissionPeer.GetNetworkPeer();
                PersistentEmpireRepresentative persistentEmpireRepresentative = player.GetComponent<PersistentEmpireRepresentative>();
                if (persistentEmpireRepresentative == null) return false;

                bool playerHasAllItems = this.receipt.All(r => persistentEmpireRepresentative.GetInventory().IsInventoryIncludes(r.RepairItem, r.NeededCount));
                if (!playerHasAllItems)
                {
                    InformationComponent.Instance.SendMessage("Required Items:", 0x02ab89d9, player);
                    foreach (RepairReceipt r in this.receipt)
                    {
                        InformationComponent.Instance.SendMessage($"{r.NeededCount} * {r.RepairItem.Name}", 0x02ab89d9, player);
                    }
                    return false;
                }

                foreach (RepairReceipt r in this.receipt)
                {
                    persistentEmpireRepresentative.GetInventory().RemoveCountedItem(r.RepairItem, r.NeededCount);
                }

                InformationComponent.Instance.SendMessage($"{this.HitPoint + this.RepairDamage}/{this.MaxHitPoint}, repaired", 0x02ab89d9, player);
                this.SetHitPoint(this.HitPoint + this.RepairDamage, impactDirection);

                if (GameNetwork.IsServer)
                {
                    LoggerHelper.LogAnAction(attackerAgent.MissionPeer.GetNetworkPeer(), LogAction.PlayerRepairesTheDestructable, null, new object[] { this.GetType().Name });
                }
            }
            else
            {
                if (this.DestroyedByStoneOnly && (currentUsageItem == null || (currentUsageItem.WeaponClass != WeaponClass.Stone && currentUsageItem.WeaponClass != WeaponClass.Boulder) || !currentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.NotUsableWithOneHand)))
                {
                    damage = 0;
                }

                if (impactDirection == null)
                    impactDirection = Vec3.Zero;
                this.SetHitPoint(this.HitPoint - damage, impactDirection);

                if (GameNetwork.IsServer)
                {
                    LoggerHelper.LogAnAction(attackerAgent.MissionPeer.GetNetworkPeer(), LogAction.PlayerHitToDestructable, null, new object[] { this.GetType().Name });
                }
            }
            return false;
        }
    }
}
