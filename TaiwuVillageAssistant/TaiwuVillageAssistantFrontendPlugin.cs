using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;
using TMPro;
using GameData.Domains.Character;
using GameData.Utilities;
using GameData.Serializer;
using Config;
using System.Reflection;
using GameData.Domains.Building;
using GameData.Domains.Taiwu;

namespace TaiwuVillageAssistant
{
    [PluginConfig("TaiwuVillageAssistant", "WhiteMind", "1.0.0")]
    public class TaiwuVillageAssistantFrontendPlugin : TaiwuRemakePlugin
    {
        Harmony harmony;
        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }

        public override void Initialize()
        {
            harmony = Harmony.CreateAndPatchAll(typeof(TaiwuVillageAssistantFrontendPlugin));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_SelectChar), "OnRenderChar")]
        public static void OnRenderCharPostfix(UI_SelectChar __instance, int index, Refers refers)
        {
            var _charPrefabTypeField = typeof(UI_SelectChar).GetField("_charPrefabType", BindingFlags.NonPublic | BindingFlags.Instance);
            var _charPrefabType = (byte)_charPrefabTypeField.GetValue(__instance);
            if (_charPrefabType != 1) return;

            var _canSelectCharIdListField = typeof(UI_SelectChar).GetField("_canSelectCharIdList", BindingFlags.NonPublic | BindingFlags.Instance);
            var _canSelectCharIdList = (List<int>)_canSelectCharIdListField.GetValue(__instance);
            int charId = _canSelectCharIdList[index];

            bool initialized = refers.Names.Contains("MaximumSkillTypeIcon");
            if (!initialized)
            {

                var icon1 = refers.CGet<CImage>("SkillTypeIcon");
                var icon1Transform = icon1.gameObject.transform;
                var icon2 = GameObject.Instantiate(icon1, icon1Transform.parent);
                icon2.transform.localPosition = new Vector3(icon1Transform.localPosition.x, icon1Transform.localPosition.y - 30 - 2, icon1Transform.localPosition.z);
                refers.AddMono(icon2, "MaximumSkillTypeIcon");

                var num1 = refers.CGet<TextMeshProUGUI>("SkillTypeNum");
                var num1Transform = num1.gameObject.transform;
                var num2 = GameObject.Instantiate(num1, num1Transform.parent);
                num2.transform.localPosition = new Vector3(num1Transform.localPosition.x, num1Transform.localPosition.y - 30 - 2, num1Transform.localPosition.z);
                refers.AddMono(num2, "MaximumSkillTypeNum");

                var txt2 = GameObject.Instantiate(num1, num1Transform.parent);
                txt2.transform.localPosition = new Vector3(0, 170, num1Transform.localPosition.z);
                refers.AddMono(txt2, "WorkBuilding");
            }

            if (charId == -1)
            {

                refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").gameObject.SetActive(false);
                refers.CGet<CImage>("MaximumSkillTypeIcon").gameObject.SetActive(false);
                refers.CGet<TextMeshProUGUI>("WorkBuilding").gameObject.SetActive(false);
                return;
            }

            refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").gameObject.SetActive(true);
            refers.CGet<CImage>("MaximumSkillTypeIcon").gameObject.SetActive(true);
            refers.CGet<TextMeshProUGUI>("WorkBuilding").gameObject.SetActive(true);

            refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").SetText("", true);
            refers.CGet<CImage>("MaximumSkillTypeIcon").SetSprite(null, false, null);
            refers.CGet<TextMeshProUGUI>("WorkBuilding").SetText("", true);

            // 同时请求多个数据，在数据都拿到后再渲染 UI，不过不确定当前的实现方案是否常规？
            var callbackCalledCount = 0;
            var maxCallbackCount = 0;
            BuildingBlockData buildingBlockData = null;
            ValueTuple<sbyte, short> maxCombatSkillAttainments = new ValueTuple<sbyte, short>(-1, -1);
            int[] lifeSkillAttainments = null;

            var _buildingModelField = typeof(UI_SelectChar).GetField("_buildingModel", BindingFlags.NonPublic | BindingFlags.Instance);
            var _buildingModel = (BuildingModel)_buildingModelField.GetValue(__instance);
            if (_buildingModel.VillagerWork.ContainsKey(charId))
            {
                VillagerWorkData workData = _buildingModel.VillagerWork[charId];
                bool isTaiwuVillage = workData.BlockTemplateId == 0;
                if (isTaiwuVillage)
                {
                    var key = new BuildingBlockKey(workData.AreaId, workData.BlockId, workData.BuildingBlockIndex);
                    maxCallbackCount++;
                    BuildingDomainHelper.AsyncMethodCall.GetBuildingBlockData(__instance, key, delegate (int offset, RawDataPool pool)
                    {
                        buildingBlockData = new BuildingBlockData();
                        Serializer.Deserialize(pool, offset, ref buildingBlockData);
                        if (++callbackCalledCount >= maxCallbackCount)
                        {
                            OnRenderCharDataPrepared(__instance, index, refers, buildingBlockData, maxCombatSkillAttainments, lifeSkillAttainments);
                        }
                    });
                }
            }

            maxCallbackCount++;
            CharacterDomainHelper.AsyncMethodCall.GetCharacterMaxCombatSkillAttainment(__instance, charId, delegate (int offset, RawDataPool dataPool)
            {
                Serializer.Deserialize(dataPool, offset, ref maxCombatSkillAttainments);
                if (++callbackCalledCount >= maxCallbackCount)
                {
                    OnRenderCharDataPrepared(__instance, index, refers, buildingBlockData, maxCombatSkillAttainments, lifeSkillAttainments);
                }
            });

            maxCallbackCount++;
            CharacterDomainHelper.AsyncMethodCall.GetAllLifeSkillAttainment(__instance, charId, delegate (int offset2, RawDataPool dataPool2)
            {
                Serializer.Deserialize(dataPool2, offset2, ref lifeSkillAttainments);
                if (++callbackCalledCount >= maxCallbackCount)
                {
                    OnRenderCharDataPrepared(__instance, index, refers, buildingBlockData, maxCombatSkillAttainments, lifeSkillAttainments);
                }
            });
        }

        public static void OnRenderCharDataPrepared(UI_SelectChar __instance, int index, Refers refers, BuildingBlockData buildingBlockData, ValueTuple<sbyte, short> maxCombatSkillAttainments, int[] lifeSkillAttainments)
        {
            int maxLifeSkillValue = lifeSkillAttainments.Max();
            int maxLifeSkillType = lifeSkillAttainments.ToList().IndexOf(maxLifeSkillValue);

            bool maxSkillIsLifeSkill = maxLifeSkillValue > maxCombatSkillAttainments.Item2;
            var maxSkillType = maxSkillIsLifeSkill ? maxLifeSkillType : maxCombatSkillAttainments.Item1;
            var maxSkillValue = maxSkillIsLifeSkill ? maxLifeSkillValue : maxCombatSkillAttainments.Item2;
            var icon = maxSkillIsLifeSkill ? Config.LifeSkillType.Instance[maxSkillType].DisplayIcon : CombatSkillType.Instance[maxSkillType].DisplayIcon;
            var name = maxSkillIsLifeSkill ? Config.LifeSkillType.Instance[maxSkillType].Name : CombatSkillType.Instance[maxSkillType].Name;
            refers.CGet<CImage>("MaximumSkillTypeIcon").SetSprite(icon, false, null);
            refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").SetText(name + ":" + maxSkillValue.ToString(), true);

            // 人物最高造诣为当前需求造诣时高亮
            var _skillTypeField = typeof(UI_SelectChar).GetField("_skillType", BindingFlags.NonPublic | BindingFlags.Instance);
            var _skillType = (sbyte)_skillTypeField.GetValue(__instance);
            var _isLifeSkillField = typeof(UI_SelectChar).GetField("_isLifeSkill", BindingFlags.NonPublic | BindingFlags.Instance);
            var _isLifeSkill = (bool)_isLifeSkillField.GetValue(__instance);
            // if (refers.CGet<CImage>("MaximumSkillTypeIcon").sprite.name == refers.CGet<CImage>("SkillTypeIcon").sprite.name)
            if (maxSkillIsLifeSkill == _isLifeSkill && maxSkillType == _skillType)
            {
                refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").text = refers.CGet<TextMeshProUGUI>("MaximumSkillTypeNum").text.SetColor("lightblue");
            }

            if (buildingBlockData != null)
            {
                refers.CGet<TextMeshProUGUI>("WorkBuilding").SetText(BuildingBlock.Instance.GetItem(buildingBlockData.TemplateId).Name, true);
                // 当前工作建筑的需求造诣为人物最高造诣时高亮
                var isLifeSkill = buildingBlockData.ConfigData.RequireLifeSkillType != -1;
                var skillType = isLifeSkill ? buildingBlockData.ConfigData.RequireLifeSkillType : buildingBlockData.ConfigData.RequireCombatSkillType;
                if (maxSkillIsLifeSkill == isLifeSkill && maxSkillType == skillType)
                {
                    refers.CGet<TextMeshProUGUI>("WorkBuilding").text = refers.CGet<TextMeshProUGUI>("WorkBuilding").text.SetColor("lightblue");
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(InfinityScroll), "InitContainer")]
        public static void InitContainerPrefix(InfinityScroll __instance)
        {
            if (__instance.name != "CharacterScrollSkillInfo") return;

            RectTransform rectPrefab = __instance.SrcPrefab.GetComponent<RectTransform>();
            RectTransformExtensions.SetHeight(rectPrefab, 128 + 128);
        }
    }

    // https://discussions.unity.com/t/modify-the-width-and-height-of-recttransform/551868/22
    public static class RectTransformExtensions
    {
        public static Vector2 GetSize(this RectTransform source) => source.rect.size;
        public static float GetWidth(this RectTransform source) => source.rect.size.x;
        public static float GetHeight(this RectTransform source) => source.rect.size.y;

        /// <summary>
        /// Sets the sources RT size to the same as the toCopy's RT size.
        /// </summary>
        public static void SetSize(this RectTransform source, RectTransform toCopy)
        {
            source.SetSize(toCopy.GetSize());
        }

        /// <summary>
        /// Sets the sources RT size to the same as the newSize.
        /// </summary>
        public static void SetSize(this RectTransform source, Vector2 newSize)
        {
            source.SetSize(newSize.x, newSize.y);
        }

        /// <summary>
        /// Sets the sources RT size to the new width and height.
        /// </summary>
        public static void SetSize(this RectTransform source, float width, float height)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        public static void SetWidth(this RectTransform source, float width)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }

        public static void SetHeight(this RectTransform source, float height)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
