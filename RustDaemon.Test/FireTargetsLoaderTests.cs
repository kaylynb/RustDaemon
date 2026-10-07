using RustDaemon.Targeting;

namespace RustDaemon.Test;

public sealed class FireTargetsLoaderTests
{
	[Fact]
	public async Task LoadAsync_ParsesPairs_AndIgnoresCommentsAndBlanks()
	{
		var path = WriteTempFile("# targets\n\n-8,0\n10.5,-5.25\n");
		try
		{
			var targets = await FireTargetsLoader.LoadAsync(path, TestContext.Current.CancellationToken);

			Assert.Equal(2, targets.Count);
			Assert.Equal(-8, targets[0].YawDegrees);
			Assert.Equal(0, targets[0].PitchDegrees);
			Assert.Equal(10.5, targets[1].YawDegrees);
			Assert.Equal(-5.25, targets[1].PitchDegrees);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task LoadAsync_RejectsMalformedLine() => await AssertRejected("8\n");

	[Theory]
	[InlineData("180,0")]
	[InlineData("-181,0")]
	[InlineData("350,0")]
	public async Task LoadAsync_RejectsYawOutsideTheCanonicalRange(string line) => await AssertRejected(line);

	[Theory]
	[InlineData("0,76")]
	[InlineData("0,-46")]
	[InlineData("0,90")]
	public async Task LoadAsync_RejectsUnreachablePitch(string line) => await AssertRejected(line);

	[Theory]
	[InlineData("NaN,0")]
	[InlineData("0,Infinity")]
	public async Task LoadAsync_RejectsNonFiniteCoordinates(string line) => await AssertRejected(line);

	private static async Task AssertRejected(string contents)
	{
		var path = WriteTempFile(contents);
		try
		{
			await Assert.ThrowsAsync<InvalidDataException>(() =>
				FireTargetsLoader.LoadAsync(path, TestContext.Current.CancellationToken)
			);
		}
		finally
		{
			File.Delete(path);
		}
	}

	private static string WriteTempFile(string contents)
	{
		var path = Path.Combine(Path.GetTempPath(), $"rustd-targets-{Guid.NewGuid():N}.csv");
		File.WriteAllText(path, contents);
		return path;
	}
}
