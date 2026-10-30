// Discord account linking. A site account can own exactly one Discord identity and a Discord
// identity can own exactly one site account, which is what makes the Discord signup gate useful.
exports.up = async function (knex) {
    const exists = await knex.schema.hasTable('user_discord');
    if (exists) return;

    await knex.schema.createTable('user_discord', (t) => {
        t.bigint('user_id').primary().references('id').inTable('user').onDelete('CASCADE');
        t.string('discord_id', 32).notNullable().unique();
        t.string('discord_username', 64).notNullable();
        t.boolean('is_guild_member').notNullable().defaultTo(true);
        t.timestamp('linked_at').notNullable().defaultTo(knex.fn.now());
    });
};

exports.down = async function (knex) {
    await knex.schema.dropTableIfExists('user_discord');
};
