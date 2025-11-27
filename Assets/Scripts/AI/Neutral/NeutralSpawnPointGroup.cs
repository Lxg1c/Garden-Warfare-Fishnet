using System.Collections.Generic;
using UnityEngine;

namespace AI.Neutral
{
    public class NeutralSpawnPointGroup : MonoBehaviour
    {
        public List<Neutral> spawnedNeutrals = new();

        public void RegisterNeutral(Neutral neutral)
        {
            if (!spawnedNeutrals.Contains(neutral))
            {
                spawnedNeutrals.Add(neutral);
                neutral.SetSpawnGroup(this);
            }
        }

        public void UnregisterNeutral(Neutral neutral)
        {
            if (spawnedNeutrals.Contains(neutral))
                spawnedNeutrals.Remove(neutral);
        }

        public void NotifyGroupAggro(Transform aggroSource, Transform target)
        {
            foreach (var neutral in spawnedNeutrals)
            {
                if (neutral != null &&
                    neutral.gameObject.activeInHierarchy &&
                    neutral.transform != aggroSource)
                {
                    neutral.OnGroupAggroTriggered(aggroSource, target);
                }
            }
        }
    }
}