package main

import (
	"AssetValidationV2/validate"
	"bytes"
	"log"
	"sync"
	"time"

	"github.com/gofiber/fiber/v2"
	"github.com/gofiber/fiber/v2/middleware/cors"
	"github.com/gofiber/fiber/v2/middleware/logger"
	"github.com/gofiber/fiber/v2/middleware/recover"
)

type ValidationResponse struct {
	IsValid bool `json:"isValid"`
}

var asyncValidationMux sync.Mutex
var asyncValidationCount = 0

const asyncValidationLimit = 2

func beforeValidation() {
	asyncValidationMux.Lock()
	if asyncValidationCount <= asyncValidationLimit {
		asyncValidationCount++
		asyncValidationMux.Unlock()
		return
	}
	asyncValidationMux.Unlock()

	for {
		time.Sleep(time.Millisecond * 500)
		asyncValidationMux.Lock()
		if asyncValidationCount <= asyncValidationLimit {
			asyncValidationCount++
			asyncValidationMux.Unlock()
			break
		}
		asyncValidationMux.Unlock()
	}
}

func afterValidation() {
	asyncValidationMux.Lock()
	asyncValidationCount--
	asyncValidationMux.Unlock()
}

func main() {
	app := fiber.New()

	// Add middlewares for logging and crash recovery
	app.Use(recover.New())
	app.Use(logger.New())
	app.Use(cors.New())

	app.Get("/", func(c *fiber.Ctx) error {
		return c.SendString("AssetValidationServiceV2 OK")
	})

	app.Post("/api/v1/validate-place", func(c *fiber.Ctx) error {
		beforeValidation()
		defer afterValidation()

		body := c.Body()
		log.Println("[VALIDATE PLACE] received request with size:", len(body))
		nReader := bytes.NewReader(body)
		isOk := validate.IsGameValid(nReader)
		
		if !isOk {
			log.Println("[VALIDATE PLACE] validation failed!")
		}
		
		return c.Status(200).JSON(ValidationResponse{
			IsValid: isOk,
		})
	})

	app.Post("/api/v1/validate-item", func(c *fiber.Ctx) error {
		beforeValidation()
		defer afterValidation()

		body := c.Body()
		log.Println("[VALIDATE ITEM] received request with size:", len(body))
		nReader := bytes.NewReader(body)
		isOk := validate.IsItemValid(nReader)
		
		if !isOk {
			log.Println("[VALIDATE ITEM] validation failed! The item is likely not a valid Roblox XML file.")
		}
		
		return c.Status(200).JSON(ValidationResponse{
			IsValid: isOk,
		})
	})

	log.Println("Starting AssetValidationServiceV2 on port 4300")
	log.Fatal(app.Listen(":4300"))
}