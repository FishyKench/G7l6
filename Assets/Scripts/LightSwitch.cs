using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightSwitch : MonoBehaviour,IInteractable
{
    [SerializeField] private bool usable = true;
    [SerializeField] private Light[] lights;

    private bool lightsOn = true;

    public void Interact()
    {
        print("MAMA");
        if (usable && lightsOn)
        {
            foreach (var light in lights)
            {
                light.enabled = false;
                light.GetComponentInParent<MeshRenderer>().materials[1].DisableKeyword("_EMISSION");
                lightsOn = false;
            }
        }
        else if (usable && lightsOn == false)
        {
            foreach (var light in lights)
            {
                light.enabled = true;
                light.GetComponentInParent<MeshRenderer>().materials[1].EnableKeyword("_EMISSION");
                lightsOn = true;
            }
        }
    }
}
