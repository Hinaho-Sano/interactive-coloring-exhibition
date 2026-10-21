using System;
using System.Collections.Generic;

// Animals consume the traced route in order, rather than cutting straight to the palm.
// Species-specific speed and animation information now comes from AnimalCatalog.
public sealed class AnimalCompanions
{
    public struct Point
    {
        public float X, Y, Time;
        public Point(float x, float y, float t) { X = x; Y = y; Time = t; }
    }

    public sealed class Animal
    {
        public readonly AnimalDefinition Definition;
        public bool Active, FacingRight = true, Returning, WaitForRelease;
        public int Hand;
        public float X, Y, Age, Speed, Missing, SampleAt = -100, LastX, LastY;
        public float ReturnAge, ReturnX, ReturnY, ActionAge, PetProgress;
        public AnimalAction Action = AnimalAction.Follow;
        public readonly List<Point> Route = new List<Point>();

        public int Home => Definition.HomePainting;

        public Animal(AnimalDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }
    }

    public readonly Animal[] Animals;
    public readonly List<Point>[] Trails = { new List<Point>(), new List<Point>() };
    readonly float[] lastSeen = { -100, -100 };

    public bool AnyActive
    {
        get
        {
            foreach (var animal in Animals) if (animal.Active) return true;
            return false;
        }
    }

    public AnimalCompanions() : this(AnimalCatalog.GalleryDefaults()) { }

    public AnimalCompanions(AnimalDefinition[] definitions)
    {
        if (definitions == null) throw new ArgumentNullException(nameof(definitions));
        Animals = new Animal[definitions.Length];
        for (int index = 0; index < definitions.Length; index++)
            Animals[index] = new Animal(definitions[index]);
    }

    public Animal FindByHome(int painting)
    {
        foreach (var animal in Animals)
            if (animal.Home == painting) return animal;
        return null;
    }

    public void Reset()
    {
        foreach (var animal in Animals)
        {
            animal.Active = animal.Returning = animal.WaitForRelease = false;
            animal.Route.Clear();
            animal.Age = animal.Speed = animal.Missing = animal.ReturnAge = animal.ActionAge = animal.PetProgress = 0;
            animal.SampleAt = -100;
            animal.Action = AnimalAction.Follow;
        }
        foreach (var trail in Trails) trail.Clear();
        lastSeen[0] = lastSeen[1] = -100;
    }

    static float Distance(float x, float y, float x2, float y2)
    {
        float dx = (x - x2) * 16f / 9f, dy = y - y2;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    static void SetAction(Animal animal, AnimalAction action)
    {
        if (animal.Action == action) return;
        animal.Action = action;
        animal.ActionAge = 0f;
    }

    public void Step(float dt, float time, MagicLibrary library, FieldHand left, FieldHand right)
        => Step(dt, time, library, null, left, right);

    public void Step(float dt, float time, MagicLibrary library, InteractionGestures gestures, FieldHand left, FieldHand right)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
        dt = Math.Min(.05f, dt);
        var hands = new[] { left, right };

        for (int handIndex = 0; handIndex < 2; handIndex++)
        {
            var hand = hands[handIndex];
            var trail = Trails[handIndex];
            if (hand.Active && hand.Open)
            {
                if (time - lastSeen[handIndex] > .35f) trail.Clear();
                if (trail.Count == 0 || Distance(hand.X, hand.Y, trail[trail.Count - 1].X, trail[trail.Count - 1].Y) > .008f)
                {
                    if (trail.Count > 0 && Distance(hand.X, hand.Y, trail[trail.Count - 1].X, trail[trail.Count - 1].Y) > .35f)
                        trail.Clear();
                    trail.Add(new Point(hand.X, hand.Y, time));
                    if (trail.Count > 180) trail.RemoveAt(0);
                }
                lastSeen[handIndex] = time;
            }
            while (trail.Count > 0 && time - trail[0].Time > 4) trail.RemoveAt(0);
        }

        foreach (var animal in Animals)
        {
            var picture = library.Pictures[animal.Home];
            if (animal.WaitForRelease)
            {
                picture.AwakeTime = -1;
                picture.Contact = 0;
                if (!picture.Touching) animal.WaitForRelease = false;
            }
            if (!animal.Active && !animal.WaitForRelease && picture.Awake)
            {
                animal.Active = true;
                animal.Hand = picture.Hand;
                animal.X = picture.X;
                animal.Y = picture.Y;
                animal.Age = animal.Missing = animal.ReturnAge = animal.ActionAge = animal.PetProgress = 0;
                animal.Returning = false;
                animal.SampleAt = -100;
                animal.Route.Clear();
                SetAction(animal, AnimalAction.Follow);
            }
            if (!animal.Active) continue;

            animal.Age += dt;
            animal.ActionAge += dt;
            if (!animal.Returning && (animal.Age >= 10 || animal.Missing >= 3))
            {
                animal.Returning = true;
                animal.ReturnAge = 0;
                animal.ReturnX = animal.X;
                animal.ReturnY = animal.Y;
                animal.Route.Clear();
                SetAction(animal, AnimalAction.Returning);
            }
            if (animal.Returning)
            {
                animal.ReturnAge += dt;
                float f = Math.Min(1, animal.ReturnAge / 1.6f);
                f = f * f * (3 - 2 * f);
                float previousX = animal.X, previousY = animal.Y;
                animal.X = animal.ReturnX + (picture.X - animal.ReturnX) * f;
                animal.Y = animal.ReturnY + (picture.Y - animal.ReturnY) * f;
                animal.Speed = Distance(previousX, previousY, animal.X, animal.Y) / dt;
                if (Math.Abs(animal.X - previousX) > .0001f) animal.FacingRight = animal.X > previousX;
                if (animal.ReturnAge >= 1.6f)
                {
                    animal.Active = animal.Returning = false;
                    animal.WaitForRelease = true;
                    animal.Speed = 0;
                    picture.AwakeTime = -1;
                    picture.Contact = picture.Energy = picture.Glow = 0;
                    Trails[animal.Hand].Clear();
                }
                continue;
            }

            var hand = hands[animal.Hand];
            if (!hand.Active || !hand.Open)
            {
                animal.Missing += dt;
                if (animal.Missing > 1)
                {
                    int other = 1 - animal.Hand;
                    if (hands[other].Active && hands[other].Open)
                    {
                        animal.Hand = other;
                        hand = hands[other];
                        animal.Route.Clear();
                        animal.SampleAt = -100;
                        animal.Missing = 0;
                    }
                }
            }
            else animal.Missing = 0;

            if (hand.Active && hand.Open)
            {
                if (time - animal.SampleAt > .35f || Distance(hand.X, hand.Y, animal.LastX, animal.LastY) > .35f)
                {
                    animal.Route.Clear();
                    animal.SampleAt = -100;
                }
                if (animal.SampleAt < 0 || Distance(hand.X, hand.Y, animal.LastX, animal.LastY) > .008f)
                {
                    animal.Route.Add(new Point(hand.X, hand.Y, time));
                    animal.LastX = hand.X;
                    animal.LastY = hand.Y;
                    if (animal.Route.Count > 180) animal.Route.RemoveAt(0);
                }
                animal.SampleAt = time;
            }
            else if (animal.Missing > .35f) animal.Route.Clear();

            HandAction handAction = gestures == null ? HandAction.Follow : gestures[animal.Hand];
            float handDistance = hand.Active ? Distance(animal.X, animal.Y, hand.X, hand.Y) : 10f;
            if (handAction == HandAction.Tease && handDistance <= .52f)
            {
                animal.PetProgress = 0f;
                SetAction(animal, AnimalAction.Teased);
            }
            else if (handAction == HandAction.Pet && handDistance <= .16f)
            {
                animal.PetProgress += dt;
                SetAction(animal, animal.PetProgress >= .18f ? AnimalAction.Petted : AnimalAction.Follow);
            }
            else if (handAction == HandAction.Rest && handDistance <= .24f)
            {
                animal.PetProgress = 0f;
                SetAction(animal, AnimalAction.Resting);
            }
            else
            {
                animal.PetProgress = Math.Max(0f, animal.PetProgress - dt * 2f);
                SetAction(animal, AnimalAction.Follow);
            }

            if (animal.Action == AnimalAction.Petted || animal.Action == AnimalAction.Resting)
            {
                animal.Route.Clear();
                animal.Speed = 0f;
                continue;
            }

            // A teased cat should visibly watch and pounce instead of looking
            // like ordinary follow movement. The rendering loop supplies the
            // crouch/paw/pounce cycle while the animal holds its ground.
            if (animal.Action == AnimalAction.Teased)
            {
                animal.Route.Clear();
                animal.Speed = 0f;
                if (hand.Active && Math.Abs(hand.X - animal.X) > .01f)
                    animal.FacingRight = hand.X > animal.X;
                continue;
            }

            float beforeX = animal.X, beforeY = animal.Y;
            float routeDelay = .22f;
            float budget = dt * animal.Definition.MoveSpeed;
            while (animal.Route.Count > 0 && budget > 0 && animal.Age > .25f)
            {
                var point = animal.Route[0];
                if (time - point.Time < routeDelay) break;
                float distance = Distance(animal.X, animal.Y, point.X, point.Y);
                if (distance <= budget)
                {
                    animal.X = point.X;
                    animal.Y = point.Y;
                    budget -= distance;
                    animal.Route.RemoveAt(0);
                }
                else
                {
                    float f = budget / distance;
                    animal.X += (point.X - animal.X) * f;
                    animal.Y += (point.Y - animal.Y) * f;
                    budget = 0;
                }
            }
            animal.Speed = Distance(beforeX, beforeY, animal.X, animal.Y) / dt;
            if (Math.Abs(animal.X - beforeX) > .0001f) animal.FacingRight = animal.X > beforeX;
        }
    }
}
