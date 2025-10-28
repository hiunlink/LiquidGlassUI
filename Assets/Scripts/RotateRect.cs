using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateRect : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        // 让物体绕 Z 轴旋转
        transform.Rotate(0f, 0f, 30f * Time.deltaTime);
    }
}
