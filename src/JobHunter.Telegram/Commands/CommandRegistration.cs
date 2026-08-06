namespace JobHunter.Telegram.Commands;

/// <summary>
/// One entry in the command set (T11): the <c>/token</c> the router matches, a one-line description shown in
/// the <c>/help</c> list, and the <see cref="ICommandHandler"/> that runs it. The help list is built from
/// these registrations, so a command that is routable is always listed and a listed command is always
/// routable — the two cannot drift (contract §Commands).
/// </summary>
/// <param name="Token">The command token including the leading slash, e.g. <c>/digest</c>.</param>
/// <param name="Description">The one-line summary shown next to the token in <c>/help</c>.</param>
/// <param name="Handler">The handler invoked when this token leads a message.</param>
internal sealed record CommandRegistration(string Token, string Description, ICommandHandler Handler);
