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
                lightsOn = false;
            }
        }
        else if (usable && lightsOn == false)
        {
            foreach (var light in lights)
            {
                light.enabled = true;
                lightsOn = true;
            }
        }
    }
}
