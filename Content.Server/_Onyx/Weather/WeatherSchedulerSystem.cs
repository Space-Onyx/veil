namespace Content.Server._DV.Weather;

public sealed partial class WeatherSchedulerSystem
{
    private int PickRandomStage(WeatherSchedulerComponent comp)
    {
        var totalWeight = 0f;
        foreach (var stage in comp.Stages)
        {
            if (float.IsFinite(stage.Weight) && stage.Weight > 0f)
                totalWeight += stage.Weight;
        }

        if (!float.IsFinite(totalWeight) || totalWeight <= 0f)
            return _random.Next(comp.Stages.Count);

        var roll = _random.NextFloat() * totalWeight;
        for (var i = 0; i < comp.Stages.Count; i++)
        {
            var weight = comp.Stages[i].Weight;
            if (!float.IsFinite(weight) || weight <= 0f)
                continue;

            roll -= weight;
            if (roll < 0f)
                return i;
        }

        for (var i = comp.Stages.Count - 1; i >= 0; i--)
        {
            var weight = comp.Stages[i].Weight;
            if (float.IsFinite(weight) && weight > 0f)
                return i;
        }

        return _random.Next(comp.Stages.Count);
    }
}
