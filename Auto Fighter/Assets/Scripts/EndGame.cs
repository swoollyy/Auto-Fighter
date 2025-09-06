using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndGame : MonoBehaviour
{


    Pinball pm;

    // Start is called before the first frame update
    void Start()
    {
        pm = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    void OnCollisionEnter(Collision col)
    {
        if(pm.CurrentState == PinballState.Play)
        {
            Rigidbody rb;
            rb = col.collider.GetComponent<Rigidbody>();

        pm.ChangeState(PinballState.GameOver);
        }


    }

}
