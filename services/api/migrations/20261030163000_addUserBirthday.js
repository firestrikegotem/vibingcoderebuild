// Birthday collected on the 2016 sign up form. Nullable because accounts created before this
// column existed (and accounts made through the API/invite flows) never had one.
exports.up = async function (knex) {
    const hasColumn = await knex.schema.hasColumn('user', 'birthday');
    if (hasColumn) return;

    await knex.schema.alterTable('user', (t) => {
        t.date('birthday').nullable();
    });
};

exports.down = async function (knex) {
    const hasColumn = await knex.schema.hasColumn('user', 'birthday');
    if (!hasColumn) return;

    await knex.schema.alterTable('user', (t) => {
        t.dropColumn('birthday');
    });
};
