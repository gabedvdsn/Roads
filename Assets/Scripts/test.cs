using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class test : MonoBehaviour
{
    [SerializeField] private GameObject spriteGO;

    private Sprite selfSprite;
    
    // Start is called before the first frame update
    void Start()
    {
        selfSprite = GetComponent<SpriteRenderer>().sprite;
        Sprite otherSprite = spriteGO.GetComponent<SpriteRenderer>().sprite;

        Vector3 above = new Vector3(transform.position.x,
            selfSprite.textureRect.width / selfSprite.pixelsPerUnit / 2 + otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2,
            0);

        Vector3 below = new Vector3(transform.position.x,
            -selfSprite.textureRect.height / selfSprite.pixelsPerUnit / 2 - otherSprite.textureRect.height / otherSprite.pixelsPerUnit / 2,
            0);

        Vector3 left = new Vector3(
            -selfSprite.textureRect.width / selfSprite.pixelsPerUnit / 2 - otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2,
            transform.position.y,
            0);

        Vector3 right = new Vector3(
            selfSprite.textureRect.width / selfSprite.pixelsPerUnit / 2 + otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2,
            transform.position.y,
            0);

        Instantiate(spriteGO, above, Quaternion.identity, transform);
        Instantiate(spriteGO, below, Quaternion.identity, transform);
        Instantiate(spriteGO, left, Quaternion.identity, transform);
        Instantiate(spriteGO, right, Quaternion.identity, transform);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
