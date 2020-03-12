const Discord = require("discord.js");
const client = new Discord.Client();
const http = require("http");
const secretToken = require("./token.json");
client.login(secretToken.test)
	.then(console.log("Ready!"));

const categories = ["game", "truck", "trailer", "job"];

client.on("message", message =>
{
	if (message.author.bot)
		return;

	const categorySelection = function ()
	{
		let sliced = message.content.split(" ");
		let match = categories.find(element =>
		{
			for (var i = 0; i < sliced.length; i++)
				if ((element === sliced[i] && message.content !== sliced[i]) || "?" + element === sliced[i])
					return true;
		});

		if (match)
			for (var i = 0; i < sliced.length; i++)
				message.channel.send(sliced[i]);
		//get(sliced[i]);
	};

	if (message.content === "?commands")
		message.channel.send(
			`If you want multiple categories at once, just put a space inbetween the words.\n
		Game (tells you:\nIf their game is launched,\nIf they're paused,\nTheir SDK version + their SDK plugin version\nPlus a few other misc items.),\n
		Truck (tells you: \nMake and model,\nTheir speed in KPH, \ntransmission stats, and much, much more.),\n
		Trailer (tells you: \nIf it's attached,\nIts name,\nIts mass,\nas well as its current damage.),\n
		Job (tells you: \nHow much the contract is worth,\nThe deadline,\nHow much time is left until the deadline,\nthe city + company the contract came from,\n` +
			`and the destination's city + company you give your cargo to.)`);
	else
		categorySelection();

	function get(category)
	{
		http.get("http://10.0.0.125:25555/api/ets2/telemetry",
			{
				family: 4
			}, (resp) =>
		{
			var raw = "";
			resp.setEncoding("utf8");
			resp.on("data", data =>
			{
				raw = data;
			});

			resp.on("end", () =>
			{
				let setupFunc = function ()
				{
					const setup = raw.replace(/[ { "]/g, "");
					const setup2 = setup.replace(/:/g, ": ");
					const setup3 = setup2.replace(/,/g, ",\n");
					// from top to bottom: we delete both the { and " characters
					// we then change all colons to have spaces after, as none of them are spaced with the exception of the categories
					// then replace all commas so that they have a newline at the end

					let catStartWithUpperCase = category.charAt(0)
						.toUpperCase();

					return setup3
						.replace(`${category}:`, `${catStartWithUpperCase + category.substring(1)}:\n\n`) // 1
						.substring(setup3.indexOf(category), setup3.indexOf("}", setup3.indexOf(category) + 1) + 1) // 2
						.replace("\n ", "\n"); // 3
					/*
						1. Find whatever the chosen category is and replace it with a version that starts with a capital letter
						2. Make a substring starting from the category name, and have it go until the category ends (ends at "}")
						3. In order to fix a bug caused by the category names already having spaces, we check for newlines that have a space after them, and replace them with a removed space.
					*/
				};
				const newRaw = setupFunc()
					.replace(/}/g, ""); // now we remove the }'s since we no longer need them for indexing*/

				message.channel.send(newRaw.length > 2000 ? newRaw.substring(0, 2000) : newRaw)
					.catch((err) => message.channel.send(`I'm sorry, but because of the reason below, your message could not be sent.\n\n${err.toString()}`));
			});
		});
	}
});