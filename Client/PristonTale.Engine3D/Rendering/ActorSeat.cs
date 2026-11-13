using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// A biped rider's hold on its mount, laid over whatever clip its skeleton plays: it hugs the mount from behind (see the
// constants), and the whole figure drops so its pelvis is the actor's origin (the owner raises the origin to the saddle).
// Directions come from the bind pose (toes ahead of the foot, each thigh to its own side), so the rig's axes need not be
// known. Each turn is about a fixed actor-space axis through the joint and carries the joint's whole subtree.
public sealed class ActorSeat
{
    private const string PelvisBone = "Bip01 Pelvis";
    private static readonly string[] Sides = ["L", "R"];
    // The rider hugs the mount as a child rides on a back: the back curls forward over it (from the waist and again from
    // the neck), the thighs reach forward and apart along its flanks with the shins wrapping in round its belly, and the
    // upper arms reach forward round its shoulders with the forearms wrapping in; radians.
    private const string WaistBone = "Bip01 Spine1";
    private const string NeckBone = "Bip01 Neck";
    private const float WaistCurl = .45f;
    private const float NeckCurl = .3f;
    private const float ThighForward = 1.35f;
    private const float ThighApart = .75f;
    private const float KneeDrop = .25f;
    private const float KneeWrap = 1.2f;
    private const float ArmForward = 1.4f;
    private const float ArmApart = .25f;
    private const float ElbowWrap = 1.3f;

    private readonly int _pelvis;
    private readonly Joint[] _joints;

    private ActorSeat(int pelvis, Joint[] joints)
    {
        _pelvis = pelvis;
        _joints = joints;
    }

    // Null when the skeleton is not a biped with both legs.
    public static ActorSeat TryCreate(PristonModel skeleton)
    {
        var pelvis = skeleton.ObjectIndex(PelvisBone);
        if (pelvis < 0)
        {
            return null;
        }

        var objects = skeleton.Objects;
        var parents = new int[objects.Count];
        var byName = skeleton.ObjectIndexByName();
        for (var index = 0; index < objects.Count; index++)
        {
            parents[index] = objects[index].ParentName.Length > 0 && byName.TryGetValue(objects[index].ParentName, out var parent) ? parent : -1;
        }

        var legs = new List<Joint>(4);
        var arms = new List<Joint>(4);
        var hip = objects[pelvis].Transform.Translation;
        var curl = Vector3.Zero;
        foreach (var side in Sides)
        {
            var thigh = skeleton.ObjectIndex($"Bip01 {side} Thigh");
            var calf = skeleton.ObjectIndex($"Bip01 {side} Calf");
            var foot = skeleton.ObjectIndex($"Bip01 {side} Foot");
            var toe = skeleton.ObjectIndex($"Bip01 {side} Toe0");
            if (thigh < 0 || calf < 0 || foot < 0 || toe < 0)
            {
                return null;
            }

            var forward = Flat(objects[toe].Transform.Translation - objects[foot].Transform.Translation);
            var outward = Flat(objects[thigh].Transform.Translation - hip);
            if (forward == Vector3.Zero || outward == Vector3.Zero)
            {
                return null;
            }

            var bend = Vector3.Normalize(Vector3.Cross(Vector3.Down, forward));
            var apart = Vector3.Normalize(Vector3.Cross(Vector3.Down, outward));
            // Turns a limb that points forward round toward the body's middle.
            var wrap = Vector3.Normalize(Vector3.Cross(forward, -outward));
            curl = bend;
            legs.Add(new Joint(thigh, Subtree(thigh, parents), Matrix.CreateFromAxisAngle(apart, ThighApart) * Matrix.CreateFromAxisAngle(bend, ThighForward)));
            legs.Add(new Joint(calf, Subtree(calf, parents), Matrix.CreateFromAxisAngle(bend, -KneeDrop) * Matrix.CreateFromAxisAngle(wrap, KneeWrap)));

            // A rig without arms only sits.
            var upperArm = skeleton.ObjectIndex($"Bip01 {side} UpperArm");
            var forearm = skeleton.ObjectIndex($"Bip01 {side} Forearm");
            if (upperArm >= 0 && forearm >= 0)
            {
                arms.Add(new Joint(upperArm, Subtree(upperArm, parents),
                    Matrix.CreateFromAxisAngle(apart, ArmApart) * Matrix.CreateFromAxisAngle(bend, ArmForward)));
                arms.Add(new Joint(forearm, Subtree(forearm, parents), Matrix.CreateFromAxisAngle(wrap, ElbowWrap)));
            }
        }

        // The back curls first (the clavicles hang from the neck), so the arms then reach from where the shoulders went.
        var joints = new List<Joint>(10);
        foreach (var (name, angle) in new[] { (WaistBone, WaistCurl), (NeckBone, NeckCurl) })
        {
            var bone = skeleton.ObjectIndex(name);
            if (bone >= 0)
            {
                joints.Add(new Joint(bone, Subtree(bone, parents), Matrix.CreateFromAxisAngle(curl, -angle)));
            }
        }

        joints.AddRange(legs);
        joints.AddRange(arms);
        return new ActorSeat(pelvis, joints.ToArray());
    }

    // Seats a pose in actor space: transforms holds each object's bone-to-actor matrix, evaluated for the current frame.
    public void Apply(Matrix[] transforms)
    {
        foreach (var joint in _joints)
        {
            var pivot = transforms[joint.Bone].Translation;
            var turn = Matrix.CreateTranslation(-pivot) * joint.Turn * Matrix.CreateTranslation(pivot);
            foreach (var index in joint.Carried)
            {
                transforms[index] *= turn;
            }
        }

        var drop = Matrix.CreateTranslation(0f, -transforms[_pelvis].Translation.Y, 0f);
        for (var index = 0; index < transforms.Length; index++)
        {
            transforms[index] *= drop;
        }
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.Y = 0f;
        return direction.LengthSquared() > 1e-6f ? Vector3.Normalize(direction) : Vector3.Zero;
    }

    // The joint and every object under it.
    private static int[] Subtree(int root, int[] parents)
    {
        var carried = new List<int>();
        for (var index = 0; index < parents.Length; index++)
        {
            for (var at = index; at >= 0; at = parents[at])
            {
                if (at == root)
                {
                    carried.Add(index);
                    break;
                }
            }
        }

        return carried.ToArray();
    }

    private readonly record struct Joint(int Bone, int[] Carried, Matrix Turn);
}
