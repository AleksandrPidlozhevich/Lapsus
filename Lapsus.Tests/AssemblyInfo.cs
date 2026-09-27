// Dispatcher.UIThread is process-wide; parallel RunJobs would drain another class's posts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
