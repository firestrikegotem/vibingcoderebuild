-- Clothing items (T-Shirt 2, Shirt 11, Pants 12) were given thumbnails from a renderer that could
-- not load them, so they show a plain grey mannequin or nothing at all. Their pictures are their
-- own textures, which the website now uses directly.
--
-- Dropping the rows makes the five-minute thumbnail fix loop pick the items up and draw them again.
-- Nothing else is touched: only clothing types, and only their thumbnails.

DELETE FROM asset_thumbnail
WHERE asset_id IN (
    SELECT id FROM asset WHERE asset_type IN (2, 11, 12)
);

-- Check what is left to redraw (the fix loop clears this list as it works through it):
SELECT a.id, a.name, a.asset_type
FROM asset a
LEFT JOIN asset_thumbnail t ON t.asset_id = a.id
WHERE a.asset_type IN (2, 11, 12) AND t.content_url IS NULL
ORDER BY a.id DESC;
