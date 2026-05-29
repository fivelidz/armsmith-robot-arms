ArmSmith.ProceduralArm arm = UnityEngine.Object.FindObjectOfType<ArmSmith.ProceduralArm>();
string s = "";
UnityEngine.Transform root = arm.transform;
System.Collections.Generic.Stack<UnityEngine.Transform> stack = new System.Collections.Generic.Stack<UnityEngine.Transform>();
System.Collections.Generic.Stack<int> depth = new System.Collections.Generic.Stack<int>();
stack.Push(root); depth.Push(0);
while (stack.Count > 0) {
    UnityEngine.Transform t = stack.Pop();
    int d = depth.Pop();
    bool ab = t.GetComponent<UnityEngine.ArticulationBody>() != null;
    s += new string('.', d * 2) + t.name + (ab ? " [AB]" : "") + " y=" + t.position.y.ToString("F3") + "\n";
    for (int i = t.childCount - 1; i >= 0; i--) { stack.Push(t.GetChild(i)); depth.Push(d + 1); }
}
return s;
