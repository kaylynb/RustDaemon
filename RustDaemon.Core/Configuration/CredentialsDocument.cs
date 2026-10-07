namespace RustDaemon.Configuration;

public sealed record CredentialsDocument
{
	public string Ip { get; init; } = string.Empty;
	public int Port { get; init; }
	public ulong PlayerId { get; init; }
	public int PlayerToken { get; init; }
}
