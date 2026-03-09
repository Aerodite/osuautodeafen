using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

public class AnimationManager
{
    private readonly ConcurrentQueue<Func<Task>> _animationQueue = new();
    private bool _isAnimating;

    /// <summary>
    ///     Enqueue an animation to be played. If multiple animations are queued, intermediate ones will be skipped
    /// </summary>
    /// <param name="animation"></param>
    public async Task EnqueueAnimation(Func<Task> animation)
    {
        _animationQueue.Enqueue(animation);
        await ProcessQueue();
    }

    /// <summary>
    ///     Process the animation queue, ensuring only one animation plays at a time
    /// </summary>
    private async Task ProcessQueue()
    {
        if (_isAnimating) return;

        _isAnimating = true;

        while (_animationQueue.Count > 0)
        {
            if (_animationQueue.Count > 2)
                // Skip intermediate animations but keep the last one
                while (_animationQueue.Count > 2)
                    _animationQueue.TryDequeue(out _);

            if (_animationQueue.TryDequeue(out var animation)) await animation();
        }

        _isAnimating = false;
    }
}