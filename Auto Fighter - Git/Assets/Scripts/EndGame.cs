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
        var ball = col.gameObject.GetComponent<Ball>();

        if (pm.CurrentState == PinballState.Play && ball.isActive)
        {
            pm.ballCount--;
            ball.isActive = false;

            if (pm.ballCount <= 0)
                pm.ChangeState(PinballState.GameOver);
        }

        Rigidbody rb = col.gameObject.GetComponent<Rigidbody>();

        rb.velocity = Vector3.zero;



    }

}
