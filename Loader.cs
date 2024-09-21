using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Dawnsbury;
using Dawnsbury.Audio;
using Dawnsbury.Auxiliary;
using Dawnsbury.Core;
using Dawnsbury.Core.Mechanics.Rules;
using Dawnsbury.Core.Animations;
using Dawnsbury.Core.CharacterBuilder;
using Dawnsbury.Core.CharacterBuilder.AbilityScores;
using Dawnsbury.Core.CharacterBuilder.Feats;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Common;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Spellbook;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.TrueFeatDb;
using Dawnsbury.Core.CharacterBuilder.Selections.Options;
using Dawnsbury.Core.CharacterBuilder.Spellcasting;
using Dawnsbury.Core.CombatActions;
using Dawnsbury.Core.Coroutines;
using Dawnsbury.Core.Coroutines.Options;
using Dawnsbury.Core.Coroutines.Requests;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Creatures.Parts;
using Dawnsbury.Core.Intelligence;
using Dawnsbury.Core.Mechanics;
using Dawnsbury.Core.Mechanics.Core;
using Dawnsbury.Core.Mechanics.Enumerations;
using Dawnsbury.Core.Mechanics.Targeting;
using Dawnsbury.Core.Mechanics.Targeting.TargetingRequirements;
using Dawnsbury.Core.Mechanics.Targeting.Targets;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Core.Possibilities;
using Dawnsbury.Core.Roller;
using Dawnsbury.Core.StatBlocks;
using Dawnsbury.Core.StatBlocks.Description;
using Dawnsbury.Core.Tiles;
using Dawnsbury.Display;
using Dawnsbury.Display.Illustrations;
using Dawnsbury.Display.Text;
using Dawnsbury.IO;
using Dawnsbury.Modding;
using System.Reflection.Metadata;
using Dawnsbury.Core.CharacterBuilder.FeatsDb;

namespace Dawnsbury.Mods.Backgrounds.BundleOfBackgrounds {
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static class FeatNames {
        public static Dictionary<FeatId, FeatName> feats = new Dictionary<FeatId, FeatName>();

        public enum FeatId {
            UNDERWATER_MARAUDER,
            SLIPPERY_PREY,
            ESCAPE_ARTIST,
            HEFTY_HAULER,
            DUBIOUS_KNOWLEDGE
        }

        internal static void RegisterFeatNames() {
            feats.Add(FeatId.UNDERWATER_MARAUDER, ModManager.RegisterFeatName("Underwater Marauder"));
            feats.Add(FeatId.SLIPPERY_PREY, ModManager.RegisterFeatName("Slippery Prey"));
            feats.Add(FeatId.ESCAPE_ARTIST, ModManager.RegisterFeatName("Escape Artist"));
            feats.Add(FeatId.HEFTY_HAULER, ModManager.RegisterFeatName("Hefty Hauler"));
            feats.Add(FeatId.DUBIOUS_KNOWLEDGE, ModManager.RegisterFeatName("Dubious Knowledge"));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class Loader {
        //internal static Dictionary<ModEnums.CreatureId, Func<Encounter?, Creature>> Creatures = new Dictionary<ModEnums.CreatureId, Func<Encounter?, Creature>>();

        [DawnsburyDaysModMainMethod]
        public static void LoadMod() {
            FeatNames.RegisterFeatNames();
            AddFeats(CreateGeneralFeats());
            AddFeats(CreateBackgrounds());
        }

        private static void AddFeats(IEnumerable<Feat> feats) {
            foreach (Feat feat in feats) {
                ModManager.AddFeat(feat);
            }
        }

        private static IEnumerable<Feat> CreateGeneralFeats() {
            yield return new TrueFeat(FeatNames.feats[FeatNames.FeatId.UNDERWATER_MARAUDER], 1, "", "", new Trait[] { Trait.General, Trait.Skill })
            .WithOnCreature((sheet, creature) => {
                creature.AddQEffect(new QEffect("Underwater Marauder", "You are not flat-footed while underwater, and don't take the usual penalties for using a bludgeoning or slashing melee weapon in water.") {
                    YouAcquireQEffect = (self, newEffect) => {
                        if (newEffect.Id == QEffectId.AquaticCombat) {
                            return new QEffect("Aquatic Combat", "You can't cast fire spells (but fire impulses still work).\nYou can't use slashing or bludgeoning ranged attacks.\nWeapon ranged attacks have their range increments halved.") {
                                StateCheck = (Action<QEffect>)(qfAquaticCombat =>
                                {
                                    if (qfAquaticCombat.Owner.HasTrait(Trait.Aquatic) || qfAquaticCombat.Owner.HasEffect(QEffectId.Swimming))
                                        return;
                                    qfAquaticCombat.Owner.AddQEffect(new QEffect(ExpirationCondition.Ephemeral) {
                                        Id = QEffectId.CountsAllTerrainAsDifficultTerrain
                                    });
                                }),
                                PreventTakingAction = (Func<CombatAction, string>)(action =>
                                {
                                    if (action.HasTrait(Trait.Impulse))
                                        return (string)null;
                                    if (action.HasTrait(Trait.Fire))
                                        return "You can't use fire actions underwater.";
                                    return action.HasTrait(Trait.Ranged) && action.HasTrait(Trait.Attack) && IsSlashingOrBludgeoning(action) ? "You can't use slashing or bludgeoning ranged attacks underwater." : (string)null;
                                })
                            };
                        }
                        return newEffect;
                    }
                });
            })
            .WithPrerequisite(sheet => {
                return sheet.Proficiencies.Get(Trait.Athletics) >= Proficiency.Trained;
            }, "Trained in Athletics");

            yield return new TrueFeat(FeatNames.feats[FeatNames.FeatId.SLIPPERY_PREY], 2, "", "", new Trait[] { Trait.General, Trait.Skill })
            .WithPermanentQEffect("", self => {
                self.BonusToSkillChecks = (skill, action, target) => {
                    if (action.ActionId != ActionId.Escape) {
                        return null;
                    }

                    int map = action.Owner.Actions.AttackedThisManyTimesThisTurn;
                    Trait tSkill = Trait.None;
                    if (skill == Skill.Acrobatics) {
                        tSkill = Trait.Acrobatics;
                    } else {
                        tSkill = Trait.Athletics;
                    }

                    if (map == 0 || action.Owner.Proficiencies.Get(tSkill) < Proficiency.Trained) {
                        return null;
                    }

                    int bonus = 1;
                    if (action.Owner.Proficiencies.Get(tSkill) >= Proficiency.Master) {
                        bonus = 2;
                    }

                    if (map == 1) {
                        return new Bonus(bonus, BonusType.Untyped, "Slippery Prey");
                    } else if (map > 1) {
                        return new Bonus(bonus * 2, BonusType.Untyped, "Slippery Prey");
                    }
                    return null;
                };
            })
            .WithPrerequisite(sheet => {
                return sheet.Proficiencies.Get(Trait.Athletics) >= Proficiency.Trained || sheet.Proficiencies.Get(Trait.Acrobatics) >= Proficiency.Trained;
            }, "Trained in Acrobatics or Athletics");

            yield return new TrueFeat(FeatNames.feats[FeatNames.FeatId.ESCAPE_ARTIST], 1, "", "", new Trait[] { Trait.General, Trait.Homebrew })
            .WithPermanentQEffect("", self => {
                self.BonusToSkillChecks = (skill, action, target) => {
                    if (action.ActionId != ActionId.Escape) {
                        return null;
                    }

                    return new Bonus(1, BonusType.Circumstance, "Escape Artist", true);
                };
                self.BonusToAttackRolls = (self, action, target) => {
                    if (action.ActionId != ActionId.Escape) {
                        return null;
                    }

                    return new Bonus(1, BonusType.Circumstance, "Escape Artist", true);
                };
            });

            yield return new TrueFeat(FeatNames.feats[FeatNames.FeatId.HEFTY_HAULER], 1, "You can carry more than your frame implies.", "After each encounter, you're able to haul away additional miscellaneous treasures weaker parties would find too burdonsome to loot, increasing your gold reward by 5%.", new Trait[] { Trait.General, Trait.Skill, Trait.Homebrew })
            .WithPermanentQEffect("", self => {
                self.EndOfCombat = async (self, won) => {
                    self.Owner.Battle.CampaignState.CommonGold += self.Owner.Battle.Encounter.RewardGold / 20;
                };
            }).WithPrerequisite(sheet => {
                return sheet.Proficiencies.Get(Trait.Athletics) >= Proficiency.Trained;
            }, "Trained in Athletics");

            yield return new TrueFeat(FeatNames.feats[FeatNames.FeatId.DUBIOUS_KNOWLEDGE], 1, "You can carry more than your frame implies.", "After each encounter, you're able to haul away additional miscellaneous treasures weaker parties would find too burdonsome to loot, increasing your gold reward by 5%.", new Trait[] { Trait.General, Trait.Skill, Trait.Homebrew })
            .WithPermanentQEffect("", self => {
                self.AfterYouTakeAction = async (self, action) => {
                    if (action.Name != "Recall Weakness" || action.CheckResult != CheckResult.Failure) {
                        return;
                    }

                //    if (self.Owner.Battle.AskForConfirmation(self.Owner, IllustrationName.Action, "You failed your Recall Weakness check. Would you like to use Dubious Knowledge, to randomly either upgrade or downgrade your check by one degree of success?", "Yes").GetAwaiter().GetResult()) {
                //        if (R.Coin()) {
                //            action.ChosenTargets.ChosenCreature.AddQEffect(new QEffect("Recall Weakness -1", "The creature is taking a -1 circumstance penalty to its next saving throw.") {
                //                BonusToDefenses = (self, action, defence) => {
                //                    if (defence != Defense.AC) {
                //                        return new Bonus(-1, BonusType.Circumstance, "Recall Weakness");
                //                    }
                //                    return null;
                //                },
                //                Illustration = IllustrationName.Action,
                //                Source = self.Owner,
                //                ExpiresAt = ExpirationCondition.ExpiresAtEndOfSourcesTurn,
                //                CannotExpireThisTurn = true
                //            });
                //        }
                //        action.ChosenTargets.ChosenCreature.AddQEffect(new QEffect("Recall Weakness 1", "The creature is taking a +1 circumstance bonus to its next saving throw.") {
                //            BonusToDefenses = (self, action, defence) => {
                //                if (defence != Defense.AC) {
                //                    return new Bonus(1, BonusType.Circumstance, "Recall Weakness");
                //                }
                //                return null;
                //            },
                //            AfterYou
                //            Illustration = IllustrationName.Action,
                //            Source = self.Owner,
                //            ExpiresAt = ExpirationCondition.ExpiresAtEndOfSourcesTurn,
                //            CannotExpireThisTurn = true
                //        });
                //    }
                //};

                self.BeforeYourActiveRoll = ()

                //self.AdjustSavingThrowResult = (self, action, result) => {
                //    if (action.Name != "Recall Weakness" || result != CheckResult.Failure) {
                //        return result;
                //    }

                //    if (self.Owner.Battle.AskForConfirmation(self.Owner, IllustrationName.Action, "You failed your Recall Weakness check. Would you like to use Dubious Knowledge, to randomly either upgrade or downgrade your check by one degree of success?", "Yes").GetAwaiter().GetResult()) {
                //        if (R.Coin()) {
                //            return CheckResult.Success;
                //        }
                //        return CheckResult.CriticalFailure;
                //    }

                //    return result;
                //};
            }).WithPrerequisite(sheet => {
                return sheet.Proficiencies.Get(Trait.Arcana) >= Proficiency.Trained || sheet.Proficiencies.Get(Trait.Nature) >= Proficiency.Trained ||
                sheet.Proficiencies.Get(Trait.Occultism) >= Proficiency.Trained || sheet.Proficiencies.Get(Trait.Religion) >= Proficiency.Trained ||
                sheet.Proficiencies.Get(Trait.Society) >= Proficiency.Trained;
            }, "Trained in Arcana, Nature, Occultism, Society or Religion");




        }

        private static IEnumerable<Feat> CreateBackgrounds() {
            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Sailor"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Strength, Ability.Dexterity), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Athletics);
                sheet.GrantFeat(FeatNames.feats[FeatNames.FeatId.UNDERWATER_MARAUDER]);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Guard"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Strength, Ability.Charisma), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Intimidation);
                //sheet.GrantFeat(FeatNames.feats[FeatNames.FeatId.UNDERWATER_MARAUDER]);
            })
            .WithOnCreature(creature => {
                creature.AddQEffect(new QEffect() {
                    StartOfCombat = async self => {
                        if (self.Owner.PersistentUsedUpResources.UsedUpActions.Contains("Guard Ally")) {
                            self.Name += " (Expended)";
                        }
                    },
                    ProvideContextualAction = self => {
                        if (self.Owner.PersistentUsedUpResources.UsedUpActions.Contains("Guard Ally")) {
                            return null;
                        }
                        return (ActionPossibility) new CombatAction(self.Owner, IllustrationName.ShieldSpell, "Guard Ally", new Trait[] { }, "", Target.AdjacentFriend())
                        .WithSoundEffect(SfxName.RaiseShield)
                        .WithActionCost(0)
                        .WithEffectOnEachTarget(async (action, user, target, result) => {
                            target.AddQEffect(new QEffect("Guarded", $"+1 circumstance bonus to AC while adjacent to {user.Name}.") {
                                Source = user,
                                ExpiresAt = ExpirationCondition.ExpiresAtStartOfSourcesTurn,
                                BonusToDefenses = (self, action, defence) => {
                                    if (defence == Defense.AC && self.Owner.DistanceTo(user) <= 1) {
                                        return new Bonus(1, BonusType.Circumstance, "Guarded", true);
                                    }
                                    return null;
                                }
                            });
                            user.PersistentUsedUpResources.UsedUpActions.Add("Guard Ally");
                            self.Name += " (Expended)";
                        })
                        ;
                    }
                });
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Labourer"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Strength, Ability.Constitution), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Athletics);
                sheet.GrantFeat(FeatNames.feats[FeatNames.FeatId.HEFTY_HAULER]);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Criminal"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Dexterity, Ability.Intelligence), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Stealth);
                sheet.GrantFeat(FeatNames.feats[FeatNames.FeatId.ESCAPE_ARTIST]);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Despatch Runner"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Strength, Ability.Constitution), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Athletics);
                sheet.GrantFeat(FeatName.Fleet);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Scout"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Dexterity, Ability.Wisdom), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Stealth);
                sheet.GrantFeat(FeatName.Fleet);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Street Urchin"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Dexterity, Ability.Constitution), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Thievery);
                sheet.GrantFeat(FeatName.Toughness);
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Cook"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Intelligence, Ability.Constitution), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Diplomacy);
            })
            .WithOnCreature(cook => {
                cook.AddQEffect(new QEffect() {
                    StartOfCombat = async self => {
                        if (self.Owner.PersistentUsedUpResources.UsedUpActions.Contains("Gourmet Rations")) {
                            self.Name += " (Expended)";
                            return;
                        }
                        foreach (Creature ally in self.Owner.Battle.AllSpawnedCreatures.Where(cr => cr.OwningFaction.IsHumanControlled)) {
                            int chance = R.NextD20();
                            if (chance < 16) {
                                return;
                            }
                            int meal = R.Next(1, 5);
                            if (meal == 1) {
                                ally.AddQEffect(new QEffect("Fortifying Meal", "+1 bonus to fortitude saves.") {
                                    Illustration = IllustrationName.LesserHealingPotion,
                                    BonusToDefenses = (self, action, defence) => {
                                        if (defence == Defense.Fortitude) {
                                            return new Bonus(1, BonusType.Untyped, "Fortifying Meal");
                                        }
                                        return null;
                                    }
                                });
                            }
                            else if (meal == 2) {
                                ally.AddQEffect(new QEffect("Invigorating Meal", "+5-foot bonus to speed.") {
                                    Illustration = IllustrationName.LesserHealingPotion,
                                    BonusToAllSpeeds = (self) => {
                                        return new Bonus(1, BonusType.Untyped, "Invigorating Meal");
                                    }
                                });
                            }
                            else if (meal == 3) {
                                // Hearty Meal
                                ally.GainTemporaryHP(self.Owner.Level);
                            }
                            else if (meal == 4) {
                                ally.AddQEffect(new QEffect("Emboldening Meal", "+1 bonus to will saves.") {
                                    Illustration = IllustrationName.LesserHealingPotion,
                                    BonusToDefenses = (self, action, defence) => {
                                        if (defence == Defense.Will) {
                                            return new Bonus(1, BonusType.Untyped, "Emboldening Meal");
                                        }
                                        return null;
                                    }
                                });
                            }
                        }
                        self.Owner.PersistentUsedUpResources.UsedUpActions.Add("Gourmet Rations");
                        self.Name += " (Expended)";
                    },
                });
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Fire Warden"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Strength, Ability.Constitution), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Athletics);
                // From Lizardfolk mod (maybe just add it as a base feat, but remove if lizardfolk is detected?)
                sheet.GrantFeat(AllFeats.All.FirstOrDefault(ft => ft.Name == "Breath Control").FeatName); // Breath Control
            });

            yield return new BackgroundSelectionFeat(ModManager.RegisterFeatName("Hermit"), "", "", new List<AbilityBoost>() { new LimitedAbilityBoost(Ability.Constitution, Ability.Intelligence), new FreeAbilityBoost() })
            .WithOnSheet(sheet => {
                sheet.GrantFeat(FeatName.Nature);
                sheet.GrantFeat(FeatNames.feats[FeatNames.FeatId.DUBIOUS_KNOWLEDGE]);
            });

        }

        static bool IsSlashingOrBludgeoning(CombatAction action) {
            Item obj1 = action.Item;
            DamageKind? damageKind1;
            int num1;
            if (obj1 == null) {
                num1 = 0;
            } else {
                damageKind1 = obj1.WeaponProperties?.DamageKind;
                DamageKind damageKind2 = DamageKind.Slashing;
                num1 = damageKind1.GetValueOrDefault() == damageKind2 & damageKind1.HasValue ? 1 : 0;
            }
            if (num1 == 0) {
                Item obj2 = action.Item;
                int num2;
                if (obj2 == null) {
                    num2 = 0;
                } else {
                    damageKind1 = obj2.WeaponProperties?.DamageKind;
                    DamageKind damageKind3 = DamageKind.Bludgeoning;
                    num2 = damageKind1.GetValueOrDefault() == damageKind3 & damageKind1.HasValue ? 1 : 0;
                }
                if (num2 == 0)
                    return false;
            }
            Item obj3 = action.Item;
            return (obj3 != null ? (obj3.HasTrait(Trait.VersatileP) ? 1 : 0) : 0) == 0;
        }
    }
}
