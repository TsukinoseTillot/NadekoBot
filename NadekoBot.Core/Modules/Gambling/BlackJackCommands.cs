﻿using Discord;
using Discord.Commands;
using NadekoBot.Common.Attributes;
using NadekoBot.Core.Modules.Gambling.Common.Blackjack;
using NadekoBot.Core.Modules.Gambling.Services;
using NadekoBot.Core.Services;
using NadekoBot.Extensions;
using System.Linq;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Gambling
{
    public partial class Gambling
    {
        public class BlackJackCommands : NadekoSubmodule<BlackJackService>
        {
            private readonly CurrencyService _cs;
            private readonly DbService _db;
            private IUserMessage _msg;

            public enum BjAction
            {
                Hit = int.MinValue,
                Stand,
                Double,
            }

            public BlackJackCommands(CurrencyService cs, DbService db)
            {
                _cs = cs;
                _db = db;
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task BlackJack(Allin _)
            {
                long cur;
                using (var uow = _db.UnitOfWork)
                {
                    cur = uow.DiscordUsers.GetOrCreate(Context.User).CurrencyAmount;
                }

                return BlackJack(cur);
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task BlackJack(long amount)
            {
                if (amount < 0)
                    return;
                
                var newBj = new Blackjack(Context.User, amount, _cs, _db);
                Blackjack bj;
                if (newBj == (bj = _service.Games.GetOrAdd(Context.Channel.Id, newBj)))
                {
                    if(!_cs.Remove(Context.User.Id, "BlackJack-bet", amount, gamble: true, user: Context.User))
                    {
                        _service.Games.TryRemove(Context.Channel.Id, out _);
                        await ReplyErrorLocalized("not_enough").ConfigureAwait(false);
                        return;
                    }
                    bj.Start();
                    bj.StateUpdated += Bj_StateUpdated;
                    bj.GameEnded += Bj_GameEnded;

                    await ReplyConfirmLocalized("bj_created").ConfigureAwait(false);
                }
                else
                {
                    if(bj.Join(Context.User, amount))
                        await ReplyConfirmLocalized("bj_joined").ConfigureAwait(false);
                }

                await Context.Message.DeleteAsync();
            }

            private Task Bj_GameEnded(Blackjack arg)
            {
                _service.Games.TryRemove(Context.Channel.Id, out _);
                return Task.CompletedTask;
            }

            private async Task Bj_StateUpdated(Blackjack bj)
            {
                try
                {
                    if (_msg != null)
                    {
                        var _ = _msg.DeleteAsync();
                    }

                    var c = bj.Dealer.Cards.Select(x => x.GetEmojiString());
                    var cStr = string.Concat(c.Select(x => x.Substring(0, x.Length - 1) + " "));
                    cStr += "\n" + string.Concat(c.Select(x => x.Last() + " "));
                    var embed = new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("BlackJack")
                        .AddField($"Dealer's Hand", cStr);

                    if (bj.CurrentUser != null)
                    {
                        embed.WithFooter($"Player to make a choice: {bj.CurrentUser.DiscordUser.ToString()}");
                    }

                    foreach (var p in bj.Players)
                    {
                        c = p.Cards.Select(x => x.GetEmojiString());
                        cStr = "-\t" + string.Concat(c.Select(x => x.Substring(0, x.Length - 1) + " "));
                        cStr += "\n-\t" + string.Concat(c.Select(x => x.Last() + " "));
                        var full = $"{p.DiscordUser.ToString().TrimTo(20)} | Bet: {p.Bet}";
                        if (bj.State == Blackjack.GameState.Ended)
                        {
                            if (p.State == User.UserState.Lost)
                            {
                                full = "❌ " + full;
                            }
                            else
                            {
                                full = "✅ " + full;
                            }
                        }
                        else if (p == bj.CurrentUser)
                            full = "▶ " + full;
                        else if (p.State == User.UserState.Stand)
                            full = "⏹ " + full;
                        else if (p.State == User.UserState.Bust)
                            full = "💥 " + full;
                        else if (p.State == User.UserState.Blackjack)
                            full = "💰 " + full;
                        embed.AddField(full, cStr);
                    }
                    _msg = await Context.Channel.EmbedAsync(embed);
                }
                catch
                {

                }
            }

            private string UserToString(User x)
            {
                var playerName = x.State == User.UserState.Bust ?
                    Format.Strikethrough(x.DiscordUser.ToString().TrimTo(30)) :
                    x.DiscordUser.ToString();

                var hand = $"{string.Concat(x.Cards.Select(y => "〖" + y.GetEmojiString() + "〗"))}";


                return $"{playerName} | Bet: {x.Bet}\n";
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Hit() => BlackJack(BjAction.Hit);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Stand() => BlackJack(BjAction.Stand);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Double() => BlackJack(BjAction.Double);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task BlackJack(BjAction a)
            {
                if (!_service.Games.TryGetValue(Context.Channel.Id, out var bj))
                    return;

                if (a == BjAction.Hit)
                    bj.Hit(Context.User);
                else if (a == BjAction.Stand)
                    bj.Stand(Context.User);
                else if (a == BjAction.Double)
                {
                    if(!bj.Double(Context.User))
                    {
                        await ReplyErrorLocalized("not_enough").ConfigureAwait(false);
                    }
                }

                await Context.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
    }
}
