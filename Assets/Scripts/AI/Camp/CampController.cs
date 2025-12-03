using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace AI.Camp
{
    public class CampController : NetworkBehaviour
    {
        private List<Neutral.Neutral> _neutralsList;

        private void Awake()
        {
            Camp camp = GetComponentInParent<Camp>();
        
            if (!camp)
            {
                Debug.LogError("CampController: Кемп не найден!");
                return;
            }

            camp.OnCampReady += HandleCampReady;
        }

        private void HandleCampReady(List<Neutral.Neutral> units)
        {
            _neutralsList = units;

            Debug.Log($"CampController: получено {units.Count} нейтралов");
        }
    }
}