import { useEffect, useState } from "react";
import { createContainer } from "unstated-next";
import { getAvatar, getMyAvatar, getRules, redrawMyAvatar, setColors as setColorsRequest, setWearingAssets as setWearingAssetsRequest } from "../services/avatar";
import { multiGetUserThumbnails } from "../services/thumbnails";

const CharacterCustomizationStore = createContainer(() => {
  const [rules, setRules] = useState(null);
  const [wearingAssets, setWearingAssets] = useState(null);
  const [colors, setColors] = useState(null);
  const [isRendering, setIsRendering] = useState(false);
  const [userId, setUserId] = useState(null);
  const [changes, setChanges] = useState(0);
  const [isModified, setIsModified] = useState(false);
  const [thumbnail, setThumbnail] = useState(null);

  useEffect(() => {
    if (!userId) return;
    getMyAvatar().then(result => {
      setWearingAssets(result.assets.map(v => {
        return {
          assetId: v.id,
          name: v.name,
          assetType: v.assetType,
        }
      }));
      setColors(result.bodyColors);
    })
  }, [userId]);

  useEffect(() => {
    setChanges(changes + 1);
    if (changes <= 2) return;
    setIsModified(true);
  }, [wearingAssets, colors]);

  useEffect(() => {
    if (!isRendering) return;
    setIsModified(false);
    let attempts = 0;
    const timer = setInterval(() => {
      attempts++;
      // A render that fails never reports Completed, so stop waiting eventually
      // instead of leaving the avatar stuck on "Loading..." forever.
      if (attempts > 24) {
        setIsRendering(false);
        clearInterval(timer);
        return;
      }
      multiGetUserThumbnails({
        userIds: [userId],
      }).then(result => {
        const user = result[0];
        if (user.state === 'Completed' && typeof user.imageUrl === 'string') {
          setIsRendering(false);
          setThumbnail(user.imageUrl);
          clearInterval(timer);
        } else if (user.state === 'Error' || user.state === 'Blocked') {
          setIsRendering(false);
          clearInterval(timer);
        }
      }).catch(() => {
        setIsRendering(false);
        clearInterval(timer);
      });
    }, 2500);

    return () => {
      clearInterval(timer);
    }
  }, [isRendering]);

  const requestRender = (force = false) => {
    setColorsRequest(colors).then(() => {
      setWearingAssetsRequest({ assetIds: wearingAssets.map(v => v.assetId) }).then(() => {
        if (force) {
          redrawMyAvatar().then(() => {
            setIsRendering(true);
            setThumbnail(null);
          }).catch(e => {

          });
        } else {
          setIsRendering(true);
          setThumbnail(null);
        }
      })
    })
  }

  useEffect(() => {
    getRules().then(res => {
      setRules(res);
    })
  }, []);

  return {
    rules,
    setRules,

    userId,
    setUserId,

    wearingAssets,
    setWearingAssets,

    colors,
    setColors,

    isRendering,
    setIsRendering,

    thumbnail,
    setThumbnail,

    isModified,
    setIsModified,

    requestRender,
  }
});

export default CharacterCustomizationStore;